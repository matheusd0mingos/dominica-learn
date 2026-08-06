using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>Uma nota como ela aparece em listagens e resultados de busca — sem o conteúdo.</summary>
public sealed record NotaIndexada(
    CaminhoNota Caminho,
    string Titulo,
    string Resumo,
    DateTimeOffset ModificadoEm,
    int Palavras,
    IReadOnlyList<Etiqueta> Etiquetas,
    IReadOnlyList<string> Apelidos);

/// <summary>Um acerto de busca, com o trecho que casou.</summary>
public sealed record Acerto(NotaIndexada Nota, string Trecho, double Relevancia);

/// <summary>Uma etiqueta com quantas notas a usam — o que o painel lateral mostra.</summary>
public sealed record EtiquetaContada(Etiqueta Etiqueta, int Notas);

/// <summary>Critérios de busca. Tudo opcional: combinar filtros é a forma de achar em vault grande.</summary>
public sealed record ConsultaDeBusca
{
    public string? Texto { get; init; }
    public Etiqueta? Etiqueta { get; init; }
    public string? Pasta { get; init; }
    public int Limite { get; init; } = 50;
}

/// <summary>
/// O ÍNDICE — derivado, descartável, reconstruível a partir do vault.
///
/// A regra que o nome desta interface tem de lembrar a quem for implementá-la: NADA QUE SÓ EXISTA AQUI
/// PODE SER CONHECIMENTO DO USUÁRIO. Se um dado não sobrevive a "apagar o banco e reindexar", ele não
/// pertence ao índice — pertence ao arquivo .md. Favorito, revisão espaçada, estatística de estudo: tudo
/// isso ou vai para o frontmatter da nota, ou aceita conscientemente ser perdido num rebuild.
/// </summary>
public interface IIndiceDoVault
{
    /// <summary>Lado "índice" da reconciliação: os mesmos metadados que <see cref="IRepositorioDeNotas.VarrerAsync"/>.</summary>
    Task<IReadOnlyList<EstadoDaNota>> EstadoAtualAsync(CancellationToken ct = default);

    /// <summary>Grava/atualiza a nota e as ligações que saem dela.</summary>
    Task IndexarAsync(Nota nota, IReadOnlyList<LigacaoResolvida> ligacoes, CancellationToken ct = default);

    Task RemoverAsync(CaminhoNota caminho, CancellationToken ct = default);

    /// <summary>Move a entrada preservando o que estava atrelado a ela (histórico, favoritos).</summary>
    Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default);

    Task<NotaIndexada?> ObterAsync(CaminhoNota caminho, CancellationToken ct = default);

    /// <summary>Tudo que o resolvedor precisa para montar o grafo: caminhos e apelidos.</summary>
    Task<IReadOnlyList<NotaConhecida>> NotasConhecidasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Acerto>> BuscarAsync(ConsultaDeBusca consulta, CancellationToken ct = default);

    /// <summary>Quem aponta PARA esta nota. É a pergunta que o Obsidian responde melhor que qualquer pasta.</summary>
    Task<IReadOnlyList<LigacaoResolvida>> BacklinksAsync(CaminhoNota caminho, CancellationToken ct = default);

    /// <summary>Ligações que saem desta nota, incluindo as quebradas.</summary>
    Task<IReadOnlyList<LigacaoResolvida>> LigacoesDeAsync(CaminhoNota caminho, CancellationToken ct = default);

    Task<IReadOnlyList<EtiquetaContada>> EtiquetasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<NotaIndexada>> RecentesAsync(int limite, CancellationToken ct = default);

    /// <summary>Todos os caminhos, para a árvore do vault.</summary>
    Task<IReadOnlyList<CaminhoNota>> TodosOsCaminhosAsync(CancellationToken ct = default);
}
