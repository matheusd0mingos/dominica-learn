using System.Text;
using System.Text.RegularExpressions;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Markdig;

namespace Dominica.Learn.Infrastructure.Renderizacao;

/// <summary>
/// Adaptador de <see cref="IRenderizadorDeMarkdown"/> sobre o Markdig.
///
/// A DECISÃO DE SEGURANÇA, e por que ela é assim:
///
/// `DisableHtml()` faz o Markdig ESCAPAR o HTML bruto em vez de repassá-lo. `&lt;script&gt;` vira texto
/// visível, não script executado. Isso é diferente — e melhor — que sanitizar depois: sanitizador é
/// lista de bloqueio, e lista de bloqueio é uma corrida contra quem inventa vetor novo (`&lt;svg
/// onload&gt;`, `javascript:` em href, entidade dupla). Recusar na origem não tem essa corrida.
///
/// O preço é real e vale declarar: quem cola um `&lt;iframe&gt;` de vídeo numa nota vai ver o texto do
/// iframe, não o vídeo. Para um vault de estudo isso é aceitável — e o inverso (executar HTML arbitrário
/// numa aplicação com sessão autenticada) não é.
///
/// A CSP em <c>CabecalhosDeSeguranca</c> é a segunda linha. Duas linhas de defesa porque a primeira
/// eventualmente falha.
/// </summary>
public sealed partial class RenderizadorMarkdig : IRenderizadorDeMarkdown
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()      // tabelas, listas de tarefa, autolink, rodapé
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseTaskLists()
        .DisableHtml()                // ← a linha que decide a segurança do produto
        .Build();

    public NotaRenderizada Renderizar(string markdown, Func<string, CaminhoNota?> resolver, Func<string, string?>? anexos = null)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return NotaRenderizada.Vazia;

        var texto = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

        // Frontmatter fora do corpo: ele é metadado, e mostrá-lo como um bloco de texto solto no topo de
        // toda nota renderizada é ruído em cada leitura.
        texto = RemoverFrontmatter(texto);

        // Wikilinks viram Markdown normal ANTES do Markdig. Fazer isso como extensão do pipeline seria
        // mais elegante e custaria uma classe de plumbing por sintaxe; a pré-passagem é KISS e o
        // resultado é idêntico. Ela respeita blocos de código — o mesmo cuidado do analisador.
        texto = ConverterWikilinks(texto, resolver, anexos, out var alvosQuebrados);

        var html = Markdown.ToHtml(texto, _pipeline);
        html = MarcarLinksQuebrados(html, alvosQuebrados);

        // O Markdig já emite <pre class="mermaid"> para blocos ```mermaid (é a extensão de diagramas, que
        // vem em UseAdvancedExtensions). Aqui só se DETECTA: escrever de novo essa conversão seria
        // duplicar mal o que a biblioteca faz bem. O conteúdo sai escapado, como código — o Mermaid lê
        // textContent, que desescapa no navegador sem passar por interpretação de HTML.
        var temDiagramas = html.Contains("class=\"mermaid\"", StringComparison.Ordinal);

        return new NotaRenderizada(html, temDiagramas, TemFormula().IsMatch(texto));
    }

    private static string RemoverFrontmatter(string texto)
    {
        if (!texto.StartsWith("---\n", StringComparison.Ordinal)) return texto;
        var fim = texto.IndexOf("\n---", 3, StringComparison.Ordinal);
        return fim < 0 ? texto : texto[(fim + 4)..].TrimStart('\n');
    }

    /// <summary>
    /// "[[Alvo|rótulo]]" → "[rótulo](/notas/Caminho.md)".
    ///
    /// Embed de ANEXO ("![[foto.png]]") vira imagem de verdade, quando <paramref name="anexos"/> resolve
    /// o arquivo. Embed de NOTA ("![[Outra Nota]]") continua virando link: embutir o conteúdo de outra
    /// nota é recursivo e precisa de guarda de ciclo — trabalho de verdade, não um `if`, e melhor ausente
    /// que meio-feito.
    /// </summary>
    private static string ConverterWikilinks(
        string texto, Func<string, CaminhoNota?> resolver, Func<string, string?>? anexos, out HashSet<string> quebrados)
    {
        var quebradosLocais = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder(texto.Length + 64);
        var emBloco = false;

        foreach (var linha in texto.Split('\n'))
        {
            var semIndentacao = linha.TrimStart();
            if (semIndentacao.StartsWith("```", StringComparison.Ordinal) || semIndentacao.StartsWith("~~~", StringComparison.Ordinal))
                emBloco = !emBloco;

            sb.Append(emBloco ? linha : Wikilink().Replace(linha, casamento =>
            {
                var interno = casamento.Groups["interno"].Value;
                var ehEmbed = casamento.Value.StartsWith('!');

                // O ANEXO É TENTADO ANTES da nota, e só quando o alvo tem cara de arquivo. Sem essa
                // condição, "[[Licitações]]" viraria uma consulta de disco a cada wikilink de nota.
                if (anexos is not null)
                {
                    var barraRotulo = interno.IndexOf('|');
                    var alvoAnexo = (barraRotulo >= 0 ? interno[..barraRotulo] : interno).Trim();
                    var legenda = barraRotulo >= 0 ? interno[(barraRotulo + 1)..].Trim() : alvoAnexo;

                    if (TemExtensaoDeArquivo(alvoAnexo) && anexos(alvoAnexo) is { } url)
                    {
                        // O ALT/rótulo é escapado, a URL não precisa: ela é gerada aqui a partir de um
                        // caminho que o domínio já validou, não copiada do texto do usuário.
                        return ehEmbed ? $"![{Escapar(legenda)}]({url})" : $"[{Escapar(legenda)}]({url})";
                    }
                    // Anexo que não existe cai adiante e é tratado como nota quebrada — que é o que ele é:
                    // uma referência a algo que não está no vault.
                }

                var rotulo = (string?)null;
                var barra = interno.IndexOf('|');
                if (barra >= 0) { rotulo = interno[(barra + 1)..].Trim(); interno = interno[..barra]; }

                var cerquilha = interno.IndexOf('#');
                var secao = cerquilha >= 0 ? interno[(cerquilha + 1)..].Trim() : null;
                var alvo = (cerquilha >= 0 ? interno[..cerquilha] : interno).Trim();

                if (alvo.Length == 0)
                    return $"[{Escapar(rotulo ?? secao ?? "")}](#{Ancora(secao ?? "")})";   // link interno

                var destino = resolver(alvo);
                var texto2 = Escapar(rotulo ?? (secao is null ? alvo : $"{alvo} › {secao}"));
                if (destino is null)
                {
                    quebradosLocais.Add(alvo);
                    // Link quebrado continua sendo LINK: clicar nele é como se cria a nota que falta.
                    // Virar texto morto tiraria do produto o fluxo "escrevo o link, depois escrevo a nota".
                    return $"[{texto2}](/notas/novo?nome={Uri.EscapeDataString(alvo)})";
                }
                var ancora = secao is null ? string.Empty : "#" + Ancora(secao);
                return $"[{texto2}](/notas/{Uri.EscapeDataString(destino.Valor).Replace("%2F", "/", StringComparison.Ordinal)}{ancora})";
            }));
            sb.Append('\n');
        }

        quebrados = quebradosLocais;
        return sb.ToString();
    }

    private static string MarcarLinksQuebrados(string html, HashSet<string> quebrados) =>
        quebrados.Count == 0 ? html
            : html.Replace("<a href=\"/notas/novo?nome=", "<a class=\"link-quebrado\" href=\"/notas/novo?nome=", StringComparison.Ordinal);

    /// <summary>Âncora no estilo do GitHub/Obsidian: minúsculas, espaços viram hífen.</summary>
    private static string Ancora(string texto) =>
        Uri.EscapeDataString(texto.Trim().ToLowerInvariant().Replace(' ', '-'));

    /// <summary>Escapa o que quebraria a sintaxe de link do Markdown no RÓTULO.</summary>
    private static string Escapar(string texto) =>
        texto.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);

    /// <summary>
    /// O alvo parece um nome de arquivo? Só serve para decidir se vale consultar o armazém de anexos.
    ///
    /// Uma nota pode legitimamente se chamar "Lei 8.666" — por isso a checagem exige que depois do último
    /// ponto venham 1 a 5 caracteres alfanuméricos e mais nada. "Lei 8.666" tem "666" e passaria; a
    /// consulta ao armazém falha, o fluxo cai no tratamento de nota, e nada quebra. Errar aqui custa uma
    /// consulta a mais, e é por isso que a heurística pode ser simples.
    /// </summary>
    private static bool TemExtensaoDeArquivo(string alvo) => ExtensaoDeArquivo().IsMatch(alvo);

    [GeneratedRegex(@"\.[A-Za-z0-9]{1,5}$")]
    private static partial Regex ExtensaoDeArquivo();

    [GeneratedRegex(@"!?\[\[(?<interno>[^\]\n]+)\]\]")]
    private static partial Regex Wikilink();

    // $$…$$ ou $…$ — só para decidir se vale carregar o KaTeX nesta página.
    //
    // O delimitador tem de COLAR no conteúdo: "$E=mc^2$" é fórmula, "R$ 10 e R$ 20" não é. Sem essa
    // exigência, qualquer nota que fale de dinheiro duas vezes carregaria o KaTeX à toa — e é uma nota
    // sobre dinheiro a cada dez num vault de concurso. É a mesma convenção do KaTeX e do Obsidian.
    [GeneratedRegex(@"\$\$[^$]+\$\$|(?<!\$)\$(?![\s$])[^$\n]*[^\s$]\$(?!\$)")]
    private static partial Regex TemFormula();
}
