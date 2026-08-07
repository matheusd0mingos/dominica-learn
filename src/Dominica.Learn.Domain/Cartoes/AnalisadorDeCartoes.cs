using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Cartoes;

/// <summary>
/// Um cartão encontrado numa nota.
///
/// <see cref="Linha"/> é o índice da linha da PERGUNTA (base zero) e é o que permite gravar o
/// agendamento de volta no lugar exato, sem reescrever o arquivo inteiro.
/// </summary>
public sealed record Cartao(
    CaminhoNota Nota,
    string Frente,
    string Verso,
    int Linha,
    int LinhasOcupadas,
    Agendamento? Agendamento)
{
    /// <summary>Cartão sem agendamento legível é novo — vence hoje e entra na fila.</summary>
    public Agendamento AgendamentoOu(DateOnly hoje) => Agendamento ?? Cartoes.Agendamento.Novo(hoje);
}

/// <summary>
/// Acha os cartões dentro de uma nota.
///
/// O FORMATO É O DO PLUGIN obsidian-spaced-repetition, e essa é a decisão inteira. Copiar a convenção de
/// alguém em vez de inventar a própria é o que faz o mesmo arquivo continuar revisável no Obsidian, com
/// o histórico construído aqui. Um formato próprio seria mais limpo e quebraria a promessa central.
///
///     Pergunta::Resposta                  cartão de uma linha
///     Pergunta:::Resposta                 idem, mas também no sentido inverso
///     Pergunta \n ? \n Resposta           cartão de várias linhas
///     Pergunta \n ?? \n Resposta          idem, nos dois sentidos
///
/// BLOCOS DE CÓDIGO SÃO PULADOS, pelo mesmo motivo do <c>AnalisadorDeNota</c>: "a::b" aparece em C++, em
/// YAML e em anotação de tipo o tempo todo, e transformar cada um deles num cartão encheria a fila de
/// revisão de lixo que ninguém escreveu como cartão.
/// </summary>
public static class AnalisadorDeCartoes
{
    /// <summary>Separadores de uma linha, do mais longo para o mais curto — a ordem importa na busca.</summary>
    private const string InlineDuplo = ":::";
    private const string Inline = "::";

    public static IReadOnlyList<Cartao> Analisar(CaminhoNota nota, string conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo)) return [];

        var linhas = conteudo.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var cartoes = new List<Cartao>();

        var cerca = (string?)null;   // a cerca que abriu o bloco, para exigir a MESMA no fechamento

        for (var i = 0; i < linhas.Length; i++)
        {
            var linha = linhas[i];
            var semIndentacao = linha.TrimStart();

            // Cerca de código: ``` ou ~~~. Guardar QUAL abriu evita que um ~~~ feche um bloco de ```,
            // o que faria o resto da nota ser lido como se fosse código.
            if (semIndentacao.StartsWith("```", StringComparison.Ordinal) || semIndentacao.StartsWith("~~~", StringComparison.Ordinal))
            {
                var marca = semIndentacao[..3];
                if (cerca is null) cerca = marca;
                else if (cerca == marca) cerca = null;
                continue;
            }
            if (cerca is not null) continue;

            // —— multi-linha: a linha SEGUINTE é só "?" ou "??" ——
            if (i + 1 < linhas.Length && EhSeparadorDeBloco(linhas[i + 1]))
            {
                var cartao = LerBloco(nota, linhas, i);
                if (cartao is not null)
                {
                    cartoes.Add(cartao);
                    i = cartao.Linha + cartao.LinhasOcupadas - 1;
                }
                continue;
            }

            // —— uma linha: pergunta::resposta ——
            var deUmaLinha = LerInline(nota, linhas, i);
            if (deUmaLinha is not null) { cartoes.Add(deUmaLinha); continue; }

            // —— lacuna: "art. ==178== define os grupos do balanço" ——
            // Vem DEPOIS do "::" de propósito: uma linha que tem os dois é um cartão explícito com um
            // destaque no meio, e não uma lacuna. Quem escreveu "::" declarou o que queria.
            var comLacuna = LerLacuna(nota, linhas, i);
            if (comLacuna is not null) cartoes.Add(comLacuna);
        }

        return cartoes;
    }

    /// <summary>Abertura e fechamento da lacuna. É a marca de destaque do Markdown, e a que o plugin usa.</summary>
    private const string Lacuna = "==";

    /// <summary>
    /// Lê a linha como cartão de LACUNA: o trecho destacado some da frente e reaparece no verso.
    ///
    /// POR QUE ESTE FORMATO EXISTE E VALE A PENA: para decorar "o art. 178 define os grupos do balanço",
    /// escrever pergunta e resposta obriga a inventar a pergunta ("qual artigo define os grupos?") e a
    /// repetir a frase. A lacuna transforma a própria frase da nota em cartão, sem duplicar texto — e o
    /// texto continua legível na leitura, porque "==" é destaque em Markdown.
    ///
    /// VÁRIAS LACUNAS NA MESMA LINHA VIRAM UM CARTÃO SÓ, com todas escondidas. O plugin do Obsidian
    /// faria delas cartões separados, cada um com seu agendamento numa marca de vários campos. Ficar com
    /// um cartão é a escolha de não escrever um formato de marca que este código não sabe ler de volta —
    /// o arquivo continua válido nos dois lados, e o que difere é só a granularidade do agendamento.
    /// </summary>
    private static Cartao? LerLacuna(CaminhoNota nota, string[] linhas, int i)
    {
        var (texto, agendamento, ocupadas) = SepararAgendamento(linhas, i, linhas[i]);

        var pedacos = texto.Split(Lacuna);

        // Split por "==" dá partes ímpares quando os delimitadores estão emparelhados: fora, dentro,
        // fora, dentro, fora. Par significa "==" solto — provavelmente texto, não lacuna.
        if (pedacos.Length < 3 || pedacos.Length % 2 == 0) return null;

        var frente = new System.Text.StringBuilder();
        var verso = new System.Text.StringBuilder();
        var achouAlgo = false;

        for (var p = 0; p < pedacos.Length; p++)
        {
            var dentro = p % 2 == 1;
            if (dentro)
            {
                // "== ==" vazio não esconde nada e viraria um cartão sem resposta.
                if (pedacos[p].Trim().Length == 0) return null;
                achouAlgo = true;
                frente.Append("[…]");
                verso.Append(pedacos[p]);
            }
            else
            {
                frente.Append(pedacos[p]);
                verso.Append(pedacos[p]);
            }
        }

        if (!achouAlgo) return null;

        var textoFrente = LimparMarcadores(frente.ToString());
        // Uma linha que é SÓ a lacuna não tem contexto nenhum: a frente seria "[…]" e a pessoa veria um
        // cartão sem pergunta. Isso é destaque de Markdown comum, não cartão.
        if (textoFrente.Replace("[…]", "").Trim().Length == 0) return null;

        return new Cartao(nota, textoFrente, LimparMarcadores(verso.ToString()), i, ocupadas, agendamento);
    }

    private static bool EhSeparadorDeBloco(string linha)
    {
        var t = linha.Trim();
        return t is "?" or "??";
    }

    private static Cartao? LerInline(CaminhoNota nota, string[] linhas, int i)
    {
        var linha = linhas[i];

        // O agendamento pode estar na MESMA linha ou na seguinte — o plugin escreve dos dois jeitos
        // dependendo da versão. Ler os dois evita reagendar do zero um cartão que já tem histórico.
        var (texto, agendamento, ocupadas) = SepararAgendamento(linhas, i, linha);

        var posicao = texto.IndexOf(InlineDuplo, StringComparison.Ordinal);
        var tamanho = InlineDuplo.Length;
        if (posicao < 0)
        {
            posicao = texto.IndexOf(Inline, StringComparison.Ordinal);
            tamanho = Inline.Length;
        }
        if (posicao <= 0) return null;

        var frente = texto[..posicao].Trim();
        var verso = texto[(posicao + tamanho)..].Trim();
        if (frente.Length == 0 || verso.Length == 0) return null;

        // Uma linha que é só um cabeçalho ou item de lista com "::" ainda é cartão — o plugin aceita, e
        // quem escreve "- Prazo::10 dias" está escrevendo um cartão dentro de uma lista, de propósito.
        return new Cartao(nota, LimparMarcadores(frente), verso, i, ocupadas, agendamento);
    }

    private static Cartao? LerBloco(CaminhoNota nota, string[] linhas, int i)
    {
        var frente = linhas[i].Trim();
        if (frente.Length == 0) return null;

        var verso = new List<string>();
        var fim = i + 2;   // pula a pergunta e o separador
        for (; fim < linhas.Length; fim++)
        {
            var l = linhas[fim];
            // O verso termina na linha em branco ou num novo cabeçalho: sem esse limite, um cartão
            // engoliria o resto da nota inteira.
            if (l.Trim().Length == 0 || l.TrimStart().StartsWith('#')) break;
            if (l.Contains(MarcaDeAgendamento.Prefixo, StringComparison.Ordinal)) break;
            verso.Add(l);
        }
        if (verso.Count == 0) return null;

        // O agendamento vem na linha logo depois do verso
        Agendamento? agendamento = null;
        var ocupadas = fim - i;
        if (fim < linhas.Length && MarcaDeAgendamento.TentarLer(linhas[fim], out agendamento)) ocupadas++;

        return new Cartao(nota, LimparMarcadores(frente), string.Join('\n', verso).Trim(), i, ocupadas, agendamento);
    }

    private static (string Texto, Agendamento? Agendamento, int Ocupadas) SepararAgendamento(
        string[] linhas, int i, string linha)
    {
        if (MarcaDeAgendamento.TentarLer(linha, out var naMesma))
            return (MarcaDeAgendamento.Remover(linha), naMesma, 1);

        if (i + 1 < linhas.Length && MarcaDeAgendamento.TentarLer(linhas[i + 1], out var naSeguinte)
            && linhas[i + 1].TrimStart().StartsWith(MarcaDeAgendamento.Prefixo, StringComparison.Ordinal))
            return (linha, naSeguinte, 2);

        return (linha, null, 1);
    }

    /// <summary>Tira o "- ", o "* " e os "#" do começo, que são estrutura do Markdown e não a pergunta.</summary>
    private static string LimparMarcadores(string texto)
    {
        var t = texto.TrimStart();
        while (t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal))
            t = t[2..].TrimStart();
        t = t.TrimStart('#').TrimStart();
        return t.Trim();
    }
}
