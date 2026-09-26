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

/// <summary>
/// Uma citação que ainda não achou destino. <see cref="Alvo"/> é o texto cru dentro dos colchetes.
/// </summary>
public sealed record LigacaoQuebrada(CaminhoNota Onde, string Alvo);

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

    /// <summary>
    /// TODAS as ligações quebradas do vault: quem citou e o que citou.
    ///
    /// EXISTE POR CAUSA DE UM DEFEITO QUE DURAVA PARA SEMPRE. Escrever "[[Prescrição]]" antes de criar a
    /// nota é o fluxo NORMAL deste produto — a lista "Ainda por escrever" é literalmente um convite a
    /// fazer isso, e clicar num item cria a nota que faltava. Só que criar a nota reindexava apenas ELA:
    /// a nota que a citava continuava com o link quebrado no índice, e nem a reconciliação do arranque
    /// consertava (disco e índice já concordam — não há o que reconciliar). O grafo nunca desenhava a
    /// aresta e os backlinks nunca apareciam, até alguém reeditar a nota que citava, à mão.
    ///
    /// DEVOLVE O ALVO CRU, e não resolve nada: quem decide se "Prescrição" casa com a nota nova é o
    /// <see cref="Dominica.Learn.Domain.Ligacoes.ResolvedorDeWikilinks"/>, que já sabe as regras
    /// (apelido, caixa, âncora, homônima). Resolver em SQL duplicaria essa regra num segundo lugar — e
    /// duas cópias de uma regra é uma que vai ficar para trás.
    ///
    /// A CONSULTA É PEQUENA POR DEFINIÇÃO: ligação quebrada é o que a pessoa ainda não escreveu.
    /// </summary>
    Task<IReadOnlyList<LigacaoQuebrada>> LigacoesQuebradasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<EtiquetaContada>> EtiquetasAsync(CancellationToken ct = default);

    /// <summary>
    /// As etiquetas de cada nota, agrupadas por nota. É o que o painel hierárquico precisa.
    ///
    /// POR QUE NÃO BASTA O <see cref="EtiquetasAsync"/> JÁ CONTADO: o total de "#direito" não é a soma de
    /// "#direito/penal" e "#direito/tributário". Uma nota marcada com a disciplina E o tópico — que é como
    /// se marca de verdade — seria contada duas vezes. O número inflado não dá erro em lugar nenhum, e
    /// vira a medida que a pessoa usa para decidir o que estudar. Ver ArvoreDeEtiquetas.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<Etiqueta>>> EtiquetasPorNotaAsync(CancellationToken ct = default);

    Task<IReadOnlyList<NotaIndexada>> RecentesAsync(int limite, CancellationToken ct = default);

    /// <summary>Todos os caminhos, para a árvore do vault.</summary>
    Task<IReadOnlyList<CaminhoNota>> TodosOsCaminhosAsync(CancellationToken ct = default);
}
