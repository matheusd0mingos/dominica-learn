namespace Dominica.Learn.Domain.Analise;

/// <summary>Como a ligação foi escrita no texto — muda o que a interface faz com ela.</summary>
public enum FormaDaLigacao
{
    /// <summary>[[Alvo]] — o formato do Obsidian.</summary>
    Wikilink,
    /// <summary>![[Alvo]] — embute o conteúdo do alvo em vez de só apontar.</summary>
    Embed,
    /// <summary>[rótulo](caminho.md) — Markdown padrão. Um vault real tem os dois.</summary>
    Markdown,
}

/// <summary>
/// Uma ligação escrita numa nota, AINDA NÃO RESOLVIDA para um caminho de verdade.
///
/// A distinção importa: "[[Licitações]]" é uma intenção do autor, não um endereço. Pode não existir nota
/// alguma com esse nome (link quebrado — que no Obsidian é um recurso, não um erro: você escreve o link
/// antes de escrever a nota) ou podem existir duas em pastas diferentes. Resolver é trabalho do
/// <see cref="ResolvedorDeWikilinks"/>, que precisa conhecer o vault inteiro; extrair é trabalho do
/// analisador, que só conhece o texto. Misturar os dois obrigaria a reanalisar toda nota sempre que
/// qualquer outra fosse criada.
/// </summary>
public sealed record Wikilink
{
    /// <summary>O que foi escrito como destino: "Licitações", "Direito/Licitações", "arquivo.png".</summary>
    public string Alvo { get; init; }

    /// <summary>Âncora depois de "#", quando houver: [[Nota#Seção]]. Null quando ausente.</summary>
    public string? Secao { get; init; }

    /// <summary>Texto de exibição depois de "|", quando houver: [[Nota|assim aparece]]. Null quando ausente.</summary>
    public string? Rotulo { get; init; }

    public FormaDaLigacao Forma { get; init; }

    /// <summary>Índice do caractere onde a ligação começa no conteúdo. Serve para a interface destacar.</summary>
    public int Posicao { get; init; }

    /// <summary>
    /// Quantos caracteres a ligação ocupa no texto, contando a sintaxe inteira: os colchetes, o "!" do
    /// embed, a seção e o rótulo.
    ///
    /// Existe para que RENOMEAR UMA NOTA possa recortar a ligação com precisão e pôr outra no lugar. Sem
    /// isto, quem reescreve teria de reencontrar a ligação no texto — ou seja, analisar de novo, num
    /// segundo parser que um dia discordaria deste. Um vault inteiro de links quebrados é o preço dessa
    /// discordância, e ele só aparece muito depois.
    /// </summary>
    public int Comprimento { get; init; }

    public Wikilink(string alvo, FormaDaLigacao forma, int posicao, string? secao = null,
        string? rotulo = null, int comprimento = 0)
    {
        Alvo = alvo;
        Forma = forma;
        Posicao = posicao;
        Secao = secao;
        Rotulo = rotulo;
        Comprimento = comprimento;
    }

    /// <summary>O que a interface mostra: o rótulo quando existe, senão o próprio alvo.</summary>
    public string TextoExibido => Rotulo ?? Alvo;

    /// <summary>
    /// Ligações para dentro do vault apontam para notas; "http://…" e "mailto:" apontam para fora e não
    /// entram no grafo de backlinks. Sem esta distinção, o painel de backlinks encheria de URLs e a
    /// pergunta que ele responde — "o que no MEU conhecimento aponta para cá?" — ficaria sem resposta.
    /// </summary>
    public bool EhExterna =>
        Alvo.Contains("://", StringComparison.Ordinal) ||
        Alvo.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
        Alvo.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
}
