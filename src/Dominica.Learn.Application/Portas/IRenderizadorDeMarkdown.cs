using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>O HTML de uma nota, mais o que o cliente ainda precisa desenhar.</summary>
public sealed record NotaRenderizada(
    string Html,
    /// <summary>Tem bloco ```mermaid — o cliente precisa carregar o Mermaid.</summary>
    bool TemDiagramas,
    /// <summary>Tem $…$ ou $$…$$ — o cliente precisa carregar o KaTeX.</summary>
    bool TemFormulas)
{
    public static readonly NotaRenderizada Vazia = new(string.Empty, false, false);
}

/// <summary>
/// Transforma o Markdown da nota em HTML seguro para exibir.
///
/// É PORTA, e não uma chamada direta ao Markdig num componente, por duas razões — e a segunda é a que
/// importa:
///
/// 1. trocar de renderizador não deve tocar em página nenhuma;
///
/// 2. ESTE É O PONTO DE ENTRADA DE XSS DO PRODUTO INTEIRO. O conteúdo é Markdown que o usuário colou de
///    qualquer canto da internet — um resumo copiado de um site pode trazer `&lt;img onerror=...&gt;` no
///    meio. Tendo uma porta, existe UM lugar onde a regra de segurança vive e UM lugar para auditar. Se
///    cada componente chamasse o renderizador direto, a pergunta "isto está sanitizado?" passaria a ter
///    N respostas possíveis, e a errada apareceria no componente que alguém escreveu com pressa.
///
/// A regra que o adaptador tem de cumprir está escrita aqui, no contrato, e não só na implementação:
/// HTML BRUTO É ESCAPADO, NUNCA INTERPRETADO. Não é sanitização por lista de bloqueio — lista de bloqueio
/// sempre tem um item faltando. É recusa na origem.
/// </summary>
public interface IRenderizadorDeMarkdown
{
    /// <summary>
    /// Renderiza. <paramref name="resolver"/> transforma o alvo de um wikilink em caminho de nota, para
    /// que "[[Licitações]]" vire um link navegável — e para que link quebrado seja marcado como tal em
    /// vez de virar um link morto que só se descobre clicando.
    ///
    /// <paramref name="anexos"/> faz o mesmo para <c>![[foto.png]]</c>: devolve a URL por onde a
    /// aplicação serve aquele arquivo, ou null se ele não existe. É aqui, na APRESENTAÇÃO, que o embed
    /// relativo ao vault vira endereço — nunca dentro do arquivo .md, que precisa continuar valendo
    /// quando aberto no Obsidian e sem servidor nenhum no ar.
    /// </summary>
    NotaRenderizada Renderizar(string markdown, Func<string, CaminhoNota?> resolver, Func<string, string?>? anexos = null);
}
