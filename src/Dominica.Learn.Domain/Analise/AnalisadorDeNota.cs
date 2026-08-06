using System.Text;

namespace Dominica.Learn.Domain.Analise;

/// <summary>
/// Extrai da nota tudo que dá para saber olhando só o texto: título, ligações, etiquetas, cabeçalhos,
/// tarefas. É uma FUNÇÃO PURA — mesmo texto, mesmo resultado, sem relógio, sem disco, sem banco.
///
/// Ser pura é o que permite reindexar dez mil notas em paralelo, testar cada regra com uma string de
/// três linhas e trocar o índice inteiro por outro banco sem tocar aqui.
///
/// O QUE ELE ENTENDE DE MARKDOWN (e o que não entende, de propósito):
///   • blocos cercados (``` e ~~~) e código em linha (`assim`) são PULADOS — um "#include" num exemplo de
///     C não é etiqueta, e "[[i]]" num trecho de código não é ligação. Sem isso, toda nota de programação
///     poluiria o painel de etiquetas e o grafo com lixo;
///   • blocos de código por INDENTAÇÃO (4 espaços) NÃO são detectados. Distingui-los de um item de lista
///     indentado exige um parser CommonMark completo, e trazer um para dentro do domínio custaria mais do
///     que o caso resolve. A consequência é conhecida: um trecho indentado com "#algo" vira etiqueta.
///     Use bloco cercado — que é o que as notas de estudo usam de qualquer forma.
/// </summary>
public static class AnalisadorDeNota
{
    private const int LinhasDoResumo = 3;
    private const int TamanhoMaximoDoResumo = 280;

    /// <summary>
    /// Analisa o conteúdo. <paramref name="nomeDoArquivo"/> é o título de último recurso — o Obsidian usa
    /// o nome do arquivo como nome da nota, e uma nota sem título é pior que uma nota com título feio.
    /// </summary>
    public static AnaliseDaNota Analisar(string conteudo, string nomeDoArquivo)
    {
        var texto = ImpressaoDigitalNormalizador(conteudo);
        var linhas = texto.Split('\n');

        var frontmatter = Frontmatter.Ler(linhas);
        var inicioDoCorpo = frontmatter.LinhasOcupadas;

        var ligacoes = new List<Wikilink>();
        var etiquetas = new List<Etiqueta>();
        var cabecalhos = new List<Cabecalho>();
        var tarefas = new List<Tarefa>();
        var linhasDoResumo = new List<string>();
        var palavras = 0;

        // deslocamento em caracteres do início de cada linha, para posicionar as ligações no texto todo
        var deslocamento = 0;
        for (var i = 0; i < inicioDoCorpo; i++) deslocamento += linhas[i].Length + 1;

        string? cercaAberta = null;   // "```" ou "~~~" — guarda QUAL abriu: ``` não fecha um ~~~

        for (var i = inicioDoCorpo; i < linhas.Length; i++)
        {
            var linha = linhas[i];
            var inicioDaLinha = deslocamento;
            deslocamento += linha.Length + 1;

            var semIndentacao = linha.TrimStart();
            var cerca = DetectarCerca(semIndentacao);

            if (cercaAberta is not null)
            {
                // dentro do bloco: só interessa saber se esta linha o fecha
                if (cerca is not null && cerca == cercaAberta) cercaAberta = null;
                continue;
            }
            if (cerca is not null) { cercaAberta = cerca; continue; }

            // —— cabeçalho: "#" a "######" seguido de espaço ————————————————————————————————
            // O espaço é o que separa cabeçalho de etiqueta: "# Direito" é título, "#direito" é etiqueta.
            var nivel = 0;
            while (nivel < semIndentacao.Length && semIndentacao[nivel] == '#') nivel++;
            if (nivel is >= 1 and <= 6 && nivel < semIndentacao.Length && semIndentacao[nivel] == ' ')
            {
                cabecalhos.Add(new Cabecalho(nivel, semIndentacao[(nivel + 1)..].Trim(), i));
            }
            else
            {
                // —— tarefa: "- [ ]" / "* [x]" / "1. [ ]" ————————————————————————————————————
                var tarefa = LerTarefa(semIndentacao, i);
                if (tarefa is not null) tarefas.Add(tarefa);
            }

            // —— corpo da linha: ligações e etiquetas, pulando código em linha ————————————————
            foreach (var (inicio, fim) in TrechosForaDeCodigo(linha))
            {
                var trecho = linha[inicio..fim];
                ExtrairLigacoes(trecho, inicioDaLinha + inicio, ligacoes);
                ExtrairEtiquetas(trecho, etiquetas);
            }

            palavras += ContarPalavras(linha);
            if (linhasDoResumo.Count < LinhasDoResumo && linha.Trim().Length > 0 && cabecalhos.All(c => c.Linha != i))
                linhasDoResumo.Add(linha.Trim());
        }

        // etiquetas do frontmatter entram junto: "tags: [direito, penal]" é a forma que o Obsidian usa e
        // ignorá-la faria metade das notas parecer sem etiqueta nenhuma
        foreach (var chave in new[] { "tags", "tag" })
            foreach (var bruta in frontmatter.Lista(chave))
                if (Etiqueta.TentarCriar(bruta) is { } e) etiquetas.Add(e);

        var apelidos = frontmatter.Lista("aliases").Concat(frontmatter.Lista("alias"))
            .Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new AnaliseDaNota
        {
            // ORDEM DO TÍTULO: frontmatter manda (é declaração explícita do autor), depois o primeiro H1
            // (convenção quase universal), e por fim o nome do arquivo (o que o Obsidian faz).
            Titulo = PrimeiroNaoVazio(frontmatter.Texto("title"), cabecalhos.FirstOrDefault(c => c.Nivel == 1)?.Texto, nomeDoArquivo),
            Ligacoes = ligacoes,
            Etiquetas = etiquetas.Distinct().ToArray(),
            Cabecalhos = cabecalhos,
            Tarefas = tarefas,
            Apelidos = apelidos,
            Frontmatter = frontmatter,
            Palavras = palavras,
            Resumo = MontarResumo(linhasDoResumo),
        };
    }

    private static string ImpressaoDigitalNormalizador(string conteudo) =>
        (conteudo ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

    private static string? DetectarCerca(string semIndentacao) =>
        semIndentacao.StartsWith("```", StringComparison.Ordinal) ? "```"
        : semIndentacao.StartsWith("~~~", StringComparison.Ordinal) ? "~~~"
        : null;

    private static Tarefa? LerTarefa(string semIndentacao, int linha)
    {
        var s = semIndentacao;
        // marcador de lista: "-", "*", "+" ou "1." / "1)"
        var i = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '*' || s[i] == '+')) i++;
        else
        {
            var d = i;
            while (d < s.Length && char.IsDigit(s[d])) d++;
            if (d == i || d >= s.Length || (s[d] != '.' && s[d] != ')')) return null;
            i = d + 1;
        }
        if (i >= s.Length || s[i] != ' ') return null;
        i++;
        if (i + 2 >= s.Length || s[i] != '[' || s[i + 2] != ']') return null;

        var marca = s[i + 1];
        // "[ ]" pendente; qualquer outra marca ("x", "X", "/", ">") conta como resolvida — o Obsidian
        // permite estados alternativos e tratá-los como pendentes deixaria a contagem sempre errada.
        var concluida = marca != ' ';
        var texto = (i + 3 < s.Length ? s[(i + 3)..] : string.Empty).Trim();
        return new Tarefa(texto, concluida, linha);
    }

    /// <summary>
    /// Trechos da linha que NÃO estão dentro de código em linha. Devolve pares [início, fim).
    /// Uma craseada abre com N crases e fecha com a próxima sequência de exatamente N — é a regra do
    /// CommonMark, e é o que faz "``código com ` dentro``" funcionar.
    /// </summary>
    private static List<(int Inicio, int Fim)> TrechosForaDeCodigo(string linha)
    {
        var trechos = new List<(int, int)>();
        var cursor = 0;
        var i = 0;
        while (i < linha.Length)
        {
            if (linha[i] != '`') { i++; continue; }

            var abertura = i;
            var n = 0;
            while (i < linha.Length && linha[i] == '`') { n++; i++; }

            // procura o fechamento com exatamente n crases
            var j = i;
            var fechamento = -1;
            while (j < linha.Length)
            {
                if (linha[j] != '`') { j++; continue; }
                var k = j;
                var m = 0;
                while (k < linha.Length && linha[k] == '`') { m++; k++; }
                if (m == n) { fechamento = k; break; }
                j = k;
            }
            // sem fechamento, a crase é literal: o resto da linha é texto normal
            if (fechamento < 0) { i = abertura + n; continue; }

            if (abertura > cursor) trechos.Add((cursor, abertura));
            cursor = fechamento;
            i = fechamento;
        }
        if (cursor < linha.Length) trechos.Add((cursor, linha.Length));
        return trechos;
    }

    private static void ExtrairLigacoes(string trecho, int deslocamento, List<Wikilink> destino)
    {
        for (var i = 0; i < trecho.Length; i++)
        {
            // —— [[wikilink]] e ![[embed]] ————————————————————————————————————————————————
            if (trecho[i] == '[' && i + 1 < trecho.Length && trecho[i + 1] == '[')
            {
                var fim = trecho.IndexOf("]]", i + 2, StringComparison.Ordinal);
                if (fim < 0) continue;
                var interno = trecho[(i + 2)..fim];
                var ehEmbed = i > 0 && trecho[i - 1] == '!';
                if (LerAlvo(interno) is { } partes)
                {
                    var inicio = ehEmbed ? i - 1 : i;
                    destino.Add(new Wikilink(partes.Alvo,
                        ehEmbed ? FormaDaLigacao.Embed : FormaDaLigacao.Wikilink,
                        deslocamento + inicio, partes.Secao, partes.Rotulo,
                        comprimento: fim + 2 - inicio));
                }
                i = fim + 1;
                continue;
            }

            // —— [rótulo](destino) ————————————————————————————————————————————————————————
            if (trecho[i] == '[')
            {
                var fecha = trecho.IndexOf(']', i + 1);
                if (fecha < 0 || fecha + 1 >= trecho.Length || trecho[fecha + 1] != '(') continue;
                var fimUrl = trecho.IndexOf(')', fecha + 2);
                if (fimUrl < 0) continue;

                var rotulo = trecho[(i + 1)..fecha];
                var url = trecho[(fecha + 2)..fimUrl].Trim();
                // título opcional: [x](destino "título")
                var espaco = url.IndexOf(' ');
                if (espaco > 0) url = url[..espaco];
                if (url.Length > 0)
                {
                    var (alvo, secao) = SepararSecao(Desescapar(url));
                    destino.Add(new Wikilink(alvo, FormaDaLigacao.Markdown, deslocamento + i, secao,
                        rotulo.Length > 0 ? rotulo : null, comprimento: fimUrl + 1 - i));
                }
                i = fimUrl;
            }
        }
    }

    private static (string Alvo, string? Secao, string? Rotulo)? LerAlvo(string interno)
    {
        if (interno.Length == 0) return null;
        string? rotulo = null;
        var barra = interno.IndexOf('|');
        if (barra >= 0)
        {
            rotulo = interno[(barra + 1)..].Trim();
            interno = interno[..barra];
        }
        var (alvo, secao) = SepararSecao(interno.Trim());
        // "[[#Seção]]" é ligação PARA DENTRO da própria nota: alvo vazio, seção preenchida. Ela é emitida,
        // e não descartada aqui, porque existe no texto e o editor precisa saber onde está para destacar e
        // navegar. Quem decide que ela não vira destino no grafo é o resolvedor — separar as duas decisões
        // é o que impede o analisador de ter opinião sobre backlinks, que ele não tem como formar.
        if (alvo.Length == 0 && secao is null) return null;
        return (alvo, secao, string.IsNullOrEmpty(rotulo) ? null : rotulo);
    }

    private static (string Alvo, string? Secao) SepararSecao(string bruto)
    {
        var cerquilha = bruto.IndexOf('#');
        if (cerquilha < 0) return (bruto, null);
        // "#seção" sozinho é ligação para dentro da própria nota — alvo vazio, seção preenchida
        var alvo = bruto[..cerquilha].Trim();
        var secao = bruto[(cerquilha + 1)..].Trim();
        return (alvo, secao.Length > 0 ? secao : null);
    }

    private static string Desescapar(string url) =>
        url.Replace("%20", " ", StringComparison.Ordinal).Trim('<', '>');

    private static void ExtrairEtiquetas(string trecho, List<Etiqueta> destino)
    {
        for (var i = 0; i < trecho.Length; i++)
        {
            if (trecho[i] != '#') continue;

            // A etiqueta precisa começar numa FRONTEIRA. Sem isto, "http://x/y#ancora" viraria etiqueta e
            // "C#" no meio de uma frase também — dois casos que aparecem no primeiro dia de uso real.
            if (i > 0)
            {
                var anterior = trecho[i - 1];
                if (anterior == '\\') continue;                                   // escapado: \#
                if (!char.IsWhiteSpace(anterior) && anterior is not ('(' or '[' or '>' or '"' or '\'' or ',' or ';')) continue;
            }

            var j = i + 1;
            while (j < trecho.Length && !char.IsWhiteSpace(trecho[j]) && trecho[j] is not ('#' or ']' or ')' or '"' or '\'' or ',' or ';' or '!' or '?')) j++;
            if (j == i + 1) continue;

            // pontuação final não faz parte da etiqueta: "#direito." é a etiqueta seguida de ponto final
            var bruta = trecho[(i + 1)..j].TrimEnd('.', ':');
            if (Etiqueta.TentarCriar(bruta) is { } e) destino.Add(e);
            i = j - 1;
        }
    }

    private static int ContarPalavras(string linha)
    {
        var n = 0;
        var dentro = false;
        foreach (var c in linha)
        {
            if (char.IsWhiteSpace(c)) dentro = false;
            else if (!dentro) { dentro = true; n++; }
        }
        return n;
    }

    private static string MontarResumo(List<string> linhas)
    {
        if (linhas.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var l in linhas)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(l);
            if (sb.Length >= TamanhoMaximoDoResumo) break;
        }
        var texto = sb.ToString();
        return texto.Length <= TamanhoMaximoDoResumo ? texto : texto[..TamanhoMaximoDoResumo].TrimEnd() + "…";
    }

    private static string PrimeiroNaoVazio(params string?[] candidatos)
    {
        foreach (var c in candidatos)
            if (!string.IsNullOrWhiteSpace(c)) return c.Trim();
        return string.Empty;
    }
}
