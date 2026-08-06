namespace Dominica.Learn.Domain.Analise;

/// <summary>Um cabeçalho Markdown: nível 1..6 e o texto.</summary>
public sealed record Cabecalho(int Nivel, string Texto, int Linha);

/// <summary>Um item de checklist: "- [ ] fazer" / "- [x] feito".</summary>
public sealed record Tarefa(string Texto, bool Concluida, int Linha);

/// <summary>
/// Tudo que se sabe sobre uma nota olhando SÓ para o texto dela.
///
/// É o produto do <see cref="AnalisadorDeNota"/> e a única coisa que o índice precisa guardar por nota
/// além do caminho e da impressão digital. Note o que NÃO está aqui: backlinks. Backlink não é
/// propriedade de uma nota, é propriedade do conjunto — só existe olhando todas as outras. Guardá-lo
/// aqui obrigaria a reanalisar a nota A toda vez que a nota B mudasse.
/// </summary>
public sealed record AnaliseDaNota
{
    public required string Titulo { get; init; }
    public required IReadOnlyList<Wikilink> Ligacoes { get; init; }
    public required IReadOnlyList<Etiqueta> Etiquetas { get; init; }
    public required IReadOnlyList<Cabecalho> Cabecalhos { get; init; }
    public required IReadOnlyList<Tarefa> Tarefas { get; init; }
    public required IReadOnlyList<string> Apelidos { get; init; }
    public required Frontmatter Frontmatter { get; init; }

    /// <summary>Palavras do corpo (sem frontmatter). Serve para a estatística de "quanto eu escrevi".</summary>
    public required int Palavras { get; init; }

    /// <summary>Primeiras linhas do corpo, para a pré-visualização na busca e na lista de recentes.</summary>
    public required string Resumo { get; init; }

    /// <summary>Ligações que apontam para dentro do vault — as que entram no grafo.</summary>
    public IEnumerable<Wikilink> LigacoesInternas => Ligacoes.Where(l => !l.EhExterna);
}
