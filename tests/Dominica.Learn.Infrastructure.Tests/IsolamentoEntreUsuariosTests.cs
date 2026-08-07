using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Infrastructure.Indice;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Infrastructure.Tests;

/// <summary>
/// A FRONTEIRA ENTRE AS PESSOAS. Antes desta camada existir, qualquer um que se cadastrasse via — e
/// editava — o vault de todo mundo, porque havia uma pasta só e o caminho da nota era único no banco
/// inteiro. Estes testes são o que impede aquilo de voltar sem ninguém notar.
///
/// O provedor em memória basta aqui porque o que se prova é SEMÂNTICA DE FILTRO, não SQL: o filtro
/// global do EF Core age no pipeline de consulta, antes de qualquer tradução para banco. O que ele NÃO
/// cobre — índice único composto — é responsabilidade da migração, e está lá.
/// </summary>
public sealed class IsolamentoEntreUsuariosTests : IDisposable
{
    // Sqlite em memória, e não o provedor "InMemory": ExecuteDelete e ExecuteUpdate são RELACIONAIS e o
    // provedor em memória simplesmente não os suporta. São justamente as duas operações que desviam do
    // rastreador de mudanças e vão direto num DELETE/UPDATE — onde um filtro faltando apagaria a nota de
    // outra pessoa. Testar o isolamento sem elas seria testar o caminho fácil.
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");

    public IsolamentoEntreUsuariosTests()
    {
        _conexao.Open();   // a base vive enquanto a conexão viver
        using var db = Contexto();
        db.Database.EnsureCreated();
    }

    private ContextoDoIndice Contexto() =>
        new(new DbContextOptionsBuilder<ContextoDoIndice>().UseSqlite(_conexao).Options);

    /// <summary>
    /// O adaptador abre UM CONTEXTO POR OPERAÇÃO (ver IndiceEmPostgres.AbrirAsync), então o teste
    /// precisa entregar uma fábrica e não um contexto. Todos apontam para a MESMA conexão Sqlite, que é
    /// quem guarda a base — é assim que o isolamento continua sendo testado contra dados de verdade,
    /// e não contra o cache de identidade de um contexto que ninguém fecha.
    /// </summary>
    private sealed class FabricaDeTeste(SqliteConnection conexao) : IDbContextFactory<ContextoDoIndice>
    {
        public ContextoDoIndice CreateDbContext() =>
            new(new DbContextOptionsBuilder<ContextoDoIndice>().UseSqlite(conexao).Options);
    }

    private (ContextoDoIndice Db, IndiceEmPostgres Indice) Para(string apelido)
    {
        var db = Contexto();
        return (db, new IndiceEmPostgres(new FabricaDeTeste(_conexao), new UsuarioDeTeste(apelido)));
    }

    public void Dispose() => _conexao.Dispose();

    private static Nota Nota(string caminho, string conteudo) =>
        Domain.Vault.Nota.Criar(CaminhoNota.De(caminho), conteudo, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Duas_pessoas_podem_ter_a_nota_de_mesmo_caminho()
    {
        // O caso mais provável de todos num vault de concurso: duas pessoas estudam para a mesma prova e
        // criam "Direito/Licitações.md". Antes, o índice único global fazia a segunda esbarrar na primeira.
        var (_, meu) = Para("matheus");
        var (_, seu) = Para("joao");

        await meu.IndexarAsync(Nota("Direito/Licitações.md", "# O meu resumo"), []);
        await seu.IndexarAsync(Nota("Direito/Licitações.md", "# O resumo dele"), []);

        var minha = await meu.ObterAsync(CaminhoNota.De("Direito/Licitações.md"));
        var dele = await seu.ObterAsync(CaminhoNota.De("Direito/Licitações.md"));

        Assert.Equal("O meu resumo", minha!.Titulo);
        Assert.Equal("O resumo dele", dele!.Titulo);
    }

    [Fact]
    public async Task A_lista_de_caminhos_traz_so_o_que_e_meu()
    {
        var (_, meu) = Para("matheus");
        var (_, seu) = Para("joao");

        await meu.IndexarAsync(Nota("Minha.md", "# Minha"), []);
        await seu.IndexarAsync(Nota("Dele.md", "# Dele"), []);

        Assert.Equal(["Minha.md"], (await meu.TodosOsCaminhosAsync()).Select(c => c.Valor));
        Assert.Equal(["Dele.md"], (await seu.TodosOsCaminhosAsync()).Select(c => c.Valor));
    }

    [Fact]
    public async Task Nota_de_outra_pessoa_simplesmente_nao_existe()
    {
        var (_, meu) = Para("matheus");
        var (_, seu) = Para("joao");

        await seu.IndexarAsync(Nota("Segredo.md", "# Segredo dele"), []);

        // Não é "acesso negado": para mim aquela nota não existe. É a resposta certa — dizer "existe mas
        // você não pode ver" já entregaria que ela existe.
        Assert.Null(await meu.ObterAsync(CaminhoNota.De("Segredo.md")));
        Assert.Empty(await meu.EstadoAtualAsync());
    }

    [Fact]
    public async Task Backlinks_nao_atravessam_a_fronteira()
    {
        // Backlink consulta a tabela de ligações DIRETO, sem passar por notas. É o caminho onde um filtro
        // esquecido vazaria os títulos das notas de outra pessoa no painel lateral.
        var (_, meu) = Para("matheus");
        var (_, seu) = Para("joao");

        var alvo = CaminhoNota.De("Alvo.md");
        LigacaoResolvida Liga(string origem) => new()
        {
            Origem = CaminhoNota.De(origem), Alvo = "Alvo", Destino = alvo, Forma = FormaDaLigacao.Wikilink,
        };

        await meu.IndexarAsync(Nota("MinhaOrigem.md", "[[Alvo]]"), [Liga("MinhaOrigem.md")]);
        await seu.IndexarAsync(Nota("OrigemDele.md", "[[Alvo]]"), [Liga("OrigemDele.md")]);

        Assert.Equal(["MinhaOrigem.md"], (await meu.BacklinksAsync(alvo)).Select(l => l.Origem.Valor));
        Assert.Equal(["OrigemDele.md"], (await seu.BacklinksAsync(alvo)).Select(l => l.Origem.Valor));
    }

    [Fact]
    public async Task Etiquetas_nao_atravessam_a_fronteira()
    {
        // Mesma história das ligações: a nuvem de etiquetas lê a tabela direto.
        var (_, meu) = Para("matheus");
        var (_, seu) = Para("joao");

        await meu.IndexarAsync(Nota("Minha.md", "# Minha\n\n#minhaetiqueta"), []);
        await seu.IndexarAsync(Nota("Dele.md", "# Dele\n\n#etiquetadele"), []);

        Assert.DoesNotContain(await meu.EtiquetasAsync(), e => e.Etiqueta.Valor.Contains("dele"));
        Assert.DoesNotContain(await seu.EtiquetasAsync(), e => e.Etiqueta.Valor.Contains("minha"));
    }

    [Fact]
    public async Task Apagar_a_minha_nota_nao_encosta_na_de_ninguem()
    {
        // RemoverAsync usa ExecuteDelete, que gera um DELETE direto. Sem o filtro global no WHERE, apagar
        // "Direito/Licitações.md" apagaria a nota de todo mundo que tivesse esse caminho.
        var (_, meu) = Para("matheus");
        var (_, seu) = Para("joao");

        var caminho = CaminhoNota.De("Direito/Licitações.md");
        await meu.IndexarAsync(Nota(caminho.Valor, "# Meu"), []);
        await seu.IndexarAsync(Nota(caminho.Valor, "# Dele"), []);

        await meu.RemoverAsync(caminho);

        Assert.Null(await meu.ObterAsync(caminho));
        Assert.NotNull(await seu.ObterAsync(caminho));
    }

    [Fact]
    public async Task Renomear_nao_renomeia_a_nota_do_outro()
    {
        var (_, meu) = Para("matheus");
        var (_, seu) = Para("joao");

        var de = CaminhoNota.De("Antiga.md");
        await meu.IndexarAsync(Nota(de.Valor, "# Meu"), []);
        await seu.IndexarAsync(Nota(de.Valor, "# Dele"), []);

        await meu.RenomearAsync(de, CaminhoNota.De("Nova.md"));

        Assert.Equal(["Nova.md"], (await meu.TodosOsCaminhosAsync()).Select(c => c.Valor));
        Assert.Equal(["Antiga.md"], (await seu.TodosOsCaminhosAsync()).Select(c => c.Valor));
    }

    [Fact]
    public async Task Sem_usuario_declarado_o_indice_nao_devolve_nada()
    {
        // A garantia de FALHAR FECHADO. Um adaptador que esquecesse de informar o usuário devolve vazio,
        // nunca a nota de outra pessoa — porque nenhuma linha real tem usuário em branco.
        var (_, meu) = Para("matheus");
        await meu.IndexarAsync(Nota("Minha.md", "# Minha"), []);

        using var db = Contexto();   // UsuarioAtual continua "" — ninguém declarou nada
        Assert.Empty(await db.Notas.ToListAsync());
        Assert.Empty(await db.Ligacoes.ToListAsync());
        Assert.Empty(await db.Etiquetas.ToListAsync());
        Assert.Empty(await db.Revisoes.ToListAsync());
    }

    [Fact]
    public async Task Reconciliacao_de_um_nao_enxerga_o_disco_do_outro()
    {
        // EstadoAtualAsync é o lado "índice" da reconciliação. Se ele visse as notas de todo mundo, a
        // reconciliação do meu vault concluiria que as notas dos outros sumiram do disco — e as apagaria.
        var (_, meu) = Para("matheus");
        var (_, seu) = Para("joao");

        await meu.IndexarAsync(Nota("Minha.md", "# Minha"), []);
        await seu.IndexarAsync(Nota("A.md", "# A"), []);
        await seu.IndexarAsync(Nota("B.md", "# B"), []);

        Assert.Single(await meu.EstadoAtualAsync());
        Assert.Equal(2, (await seu.EstadoAtualAsync()).Count);
    }
}
