using System.Globalization;
using System.Text;
using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Ligacoes;

/// <summary>
/// Uma menção: o nome de uma nota escrito como texto comum, sem <c>[[ ]]</c> em volta.
///
/// <see cref="Posicao"/> e <see cref="Comprimento"/> são a fatia exata a substituir para transformá-la em
/// ligação; <see cref="Trecho"/> é o pedaço de frase que a tela mostra para a pessoa reconhecer o
/// contexto antes de decidir.
/// </summary>
public sealed record Mencao(int Linha, int Posicao, int Comprimento, string Texto, string Trecho);

/// <summary>
/// Acha, no texto de uma nota, onde OUTRA nota é citada pelo nome sem estar ligada.
///
/// POR QUE ISTO É O RECURSO QUE MAIS DESCOBRE CONEXÃO PERDIDA:
///
/// Ninguém escreve links enquanto pensa. Escreve-se "a prescrição tributária começa a correr…" no meio de
/// um raciocínio, e só semanas depois se cria a nota "Prescrição". Naquele momento, a nota nova nasce
/// isolada: o texto que fala dela existe, mas nada os une. As menções não ligadas são a lista de todos
/// esses encontros que já aconteceram e ficaram sem registro — e é a única funcionalidade que responde à
/// pergunta "e se eu descobrir a conexão só depois?".
///
/// O QUE É EXCLUÍDO, e por quê:
///   • prosa apenas — bloco de código, código em linha e frontmatter ficam de fora, pelas MESMAS regras
///     que valem para ligações e etiquetas (ver <see cref="AnalisadorDeNota.TrechosDeProsa"/>);
///   • o que JÁ é ligação: um <c>[[Prescrição]]</c> contém a palavra e não é menção solta;
///   • pedaço de palavra: "Prescrição" dentro de "Imprescritibilidade" não é citação da nota, e sugerir
///     ligar ali ensinaria a pessoa a ignorar o painel inteiro.
///
/// A CLASSE É PURA — recebe texto e nomes, devolve posições. Quem sabe QUAIS notas vale a pena varrer é o
/// caso de uso, que tem o índice.
/// </summary>
public static class MencoesNaoLigadas
{
    /// <summary>Contexto mostrado em volta da menção, para cada lado.</summary>
    private const int ContextoEmCaracteres = 60;

    /// <summary>
    /// <paramref name="nomes"/> é o nome da nota e seus apelidos (<c>aliases</c>): quem escreve "CF/88"
    /// no texto está citando a nota "Constituição Federal", e ignorar os apelidos deixaria de fora
    /// justamente as menções que a pessoa não lembraria de procurar.
    /// </summary>
    public static IReadOnlyList<Mencao> Encontrar(string conteudo, IEnumerable<string> nomes)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        ArgumentNullException.ThrowIfNull(nomes);

        var procurados = nomes
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            // Nome de uma ou duas letras casaria em toda página. O painel viraria ruído, e ruído num
            // painel de sugestões é pior que painel vazio: ensina a pessoa a não olhar.
            .Where(n => n.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (procurados.Count == 0) return [];

        // As ligações que JÁ existem, para não sugerir ligar o que está ligado. Vem do analisador de
        // verdade: reconhecer "[[…]]" com uma varredura própria daria certo até a primeira ligação com
        // rótulo ou seção.
        var jaLigado = AnalisadorDeNota.Analisar(conteudo, string.Empty).Ligacoes
            .Where(l => l.Comprimento > 0)
            .Select(l => (Inicio: l.Posicao, Fim: l.Posicao + l.Comprimento))
            .ToList();

        var achados = new List<Mencao>();

        foreach (var trecho in AnalisadorDeNota.TrechosDeProsa(conteudo))
        {
            var dobrado = Dobrar(trecho.Texto);

            foreach (var nome in procurados)
            {
                var alvo = Dobrar(nome);
                var de = 0;

                while (de <= dobrado.Length - alvo.Length)
                {
                    var achou = dobrado.IndexOf(alvo, de, StringComparison.Ordinal);
                    if (achou < 0) break;
                    de = achou + 1;

                    if (!EhPalavraInteira(dobrado, achou, alvo.Length)) continue;

                    var posicao = trecho.Inicio + achou;
                    if (jaLigado.Any(l => posicao >= l.Inicio && posicao < l.Fim)) continue;

                    achados.Add(new Mencao(
                        trecho.Linha, posicao, nome.Length,
                        trecho.Texto.Substring(achou, nome.Length),
                        Recortar(trecho.Texto, achou, nome.Length)));
                }
            }
        }

        // Ordenada por posição, e sem duas menções no mesmo lugar — o que acontece quando o nome e um
        // apelido casam na mesma palavra. Ligar duas vezes o mesmo trecho corromperia o texto.
        return achados
            .GroupBy(m => m.Posicao)
            .Select(g => g.OrderByDescending(m => m.Comprimento).First())
            .OrderBy(m => m.Posicao)
            .ToList();
    }

    /// <summary>
    /// Transforma a menção em ligação. Devolve null quando o texto mudou embaixo — a nota pode ter sido
    /// editada entre a tela ter listado a menção e a pessoa ter clicado, e gravar por cima da posição
    /// antiga estragaria uma frase qualquer sem avisar.
    /// </summary>
    public static string? Ligar(string conteudo, Mencao mencao, string insercao)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        ArgumentNullException.ThrowIfNull(mencao);

        var texto = conteudo.Replace("\r\n", "\n").Replace('\r', '\n');
        var fim = mencao.Posicao + mencao.Comprimento;
        if (mencao.Posicao < 0 || fim > texto.Length) return null;
        if (!string.Equals(texto[mencao.Posicao..fim], mencao.Texto, StringComparison.Ordinal)) return null;

        // O RÓTULO PRESERVA O TEXTO ORIGINAL quando ele difere do alvo — "[[Prescrição|prescrição]]".
        // Sem isso, ligar uma menção no meio de uma frase trocaria a palavra por outra com caixa
        // diferente, e a pessoa veria a própria frase mudar sozinha ao clicar num botão que prometia só
        // criar uma ligação.
        var corpo = string.Equals(insercao, mencao.Texto, StringComparison.Ordinal)
            ? insercao
            : $"{insercao}|{mencao.Texto}";

        return string.Concat(texto.AsSpan(0, mencao.Posicao), $"[[{corpo}]]", texto.AsSpan(fim));
    }

    private static bool EhPalavraInteira(string dobrado, int inicio, int comprimento)
    {
        if (inicio > 0 && EhLetraOuDigito(dobrado[inicio - 1])) return false;
        var depois = inicio + comprimento;
        return depois >= dobrado.Length || !EhLetraOuDigito(dobrado[depois]);
    }

    private static bool EhLetraOuDigito(char c) => char.IsLetterOrDigit(c);

    private static string Recortar(string texto, int inicio, int comprimento)
    {
        var de = Math.Max(0, inicio - ContextoEmCaracteres);
        var ate = Math.Min(texto.Length, inicio + comprimento + ContextoEmCaracteres);
        var recorte = texto[de..ate].Trim();
        return (de > 0 ? "…" : "") + recorte + (ate < texto.Length ? "…" : "");
    }

    /// <summary>
    /// Tira acento e caixa MANTENDO O COMPRIMENTO — um caractere entra, um caractere sai.
    ///
    /// É a diferença entre isto e o `SemAcento` da busca por nome, e ela é a razão de a função existir
    /// duas vezes: ali só interessa comparar, aqui as posições do resultado são usadas para recortar o
    /// texto ORIGINAL. Normalizar do jeito comum (decompor e jogar fora os acentos) muda o tamanho da
    /// string, e cada acento antes da menção deslocaria a fatia em um caractere — a ligação sairia
    /// comendo a letra errada.
    /// </summary>
    private static string Dobrar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto) sb.Append(char.ToLowerInvariant(SemAcento(c)));
        return sb.ToString();
    }

    private static char SemAcento(char c)
    {
        if (c < 128) return c;

        foreach (var d in c.ToString().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(d) != UnicodeCategory.NonSpacingMark)
                return d;

        return c;
    }
}
