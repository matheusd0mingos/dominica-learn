using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

public class ResolvedorDeWikilinksTests
{
    private static ResolvedorDeWikilinks Com(params string[] caminhos) =>
        new(caminhos.Select(CaminhoNota.De));

    [Fact]
    public void Resolve_pelo_nome_do_arquivo()
    {
        var r = Com("Direito/Licitações.md", "Física/Óptica.md");
        Assert.Equal("Direito/Licitações.md", r.Resolver("Licitações")!.Valor);
    }

    [Fact]
    public void Resolve_pelo_caminho_completo()
    {
        var r = Com("Direito/Licitações.md");
        Assert.Equal("Direito/Licitações.md", r.Resolver("Direito/Licitações")!.Valor);
        Assert.Equal("Direito/Licitações.md", r.Resolver("Direito/Licitações.md")!.Valor);
    }

    [Fact]
    public void Resolve_por_sufixo_de_caminho()
    {
        var r = Com("Concursos/Direito/Licitações.md");
        Assert.Equal("Concursos/Direito/Licitações.md", r.Resolver("Direito/Licitações")!.Valor);
    }

    [Fact]
    public void Resolve_por_apelido_do_frontmatter()
    {
        var r = new ResolvedorDeWikilinks([
            new NotaConhecida(CaminhoNota.De("Direito/Lei14133.md"), ["Lei 14.133", "NLL"]),
        ]);
        Assert.Equal("Direito/Lei14133.md", r.Resolver("Lei 14.133")!.Valor);
        Assert.Equal("Direito/Lei14133.md", r.Resolver("NLL")!.Valor);
    }

    [Fact]
    public void Empate_e_resolvido_por_proximidade()
    {
        // "[[Índice]]" escrito dentro de Direito/ tem de achar o índice DE DIREITO. É o que faz a
        // ferramenta parecer inteligente em vez de aleatória.
        var r = Com("Direito/Índice.md", "Física/Índice.md");
        var deDentroDeDireito = CaminhoNota.De("Direito/Licitações.md");
        Assert.Equal("Direito/Índice.md", r.Resolver("Índice", deDentroDeDireito)!.Valor);

        var deDentroDeFisica = CaminhoNota.De("Física/Lentes.md");
        Assert.Equal("Física/Índice.md", r.Resolver("Índice", deDentroDeFisica)!.Valor);
    }

    [Fact]
    public void Empate_sem_proximidade_e_estavel()
    {
        // Sem critério final determinístico, duas reindexações do MESMO vault dariam grafos diferentes —
        // e backlink que pisca é backlink em que ninguém confia.
        var a = Com("B/Nota.md", "A/Nota.md").Resolver("Nota");
        var b = Com("A/Nota.md", "B/Nota.md").Resolver("Nota");
        Assert.Equal(a!.Valor, b!.Valor);
        Assert.Equal("A/Nota.md", a.Valor);
    }

    [Fact]
    public void Alvo_inexistente_devolve_nulo_e_isso_nao_e_erro()
    {
        // Escrever [[Princípio da Legalidade]] antes de existir a nota é o jeito CERTO de usar a
        // ferramenta: o link quebrado vira a lista do que ainda falta estudar.
        Assert.Null(Com("Outra.md").Resolver("Princípio da Legalidade"));
    }

    [Fact]
    public void Resolve_um_lote_e_marca_as_quebradas()
    {
        var r = Com("Direito/Licitações.md");
        var origem = CaminhoNota.De("Resumo.md");
        var analise = Domain.Analise.AnalisadorDeNota.Analisar(
            "ver [[Licitações]] e [[NãoExiste]] e [site](https://x.com)", "Resumo");

        var resolvidas = r.Resolver(origem, analise.Ligacoes);

        Assert.Equal(2, resolvidas.Count);   // a externa não entra no grafo
        Assert.Equal("Direito/Licitações.md", resolvidas[0].Destino!.Valor);
        Assert.True(resolvidas[1].Quebrada);
    }

    [Fact]
    public void Ligacao_para_a_propria_secao_nao_vira_destino()
    {
        var r = Com("Nota.md");
        var analise = Domain.Analise.AnalisadorDeNota.Analisar("volta ao [[#Topo]]", "Nota");
        var resolvida = Assert.Single(r.Resolver(CaminhoNota.De("Nota.md"), analise.Ligacoes));
        Assert.True(resolvida.EhInterna);
        Assert.Null(resolvida.Destino);
    }

    [Fact]
    public void Nome_e_insensivel_a_maiusculas_na_resolucao()
    {
        // Ao contrário do caminho no disco: quem escreve "[[licitações]]" quer a nota "Licitações".
        // A tolerância fica na RESOLUÇÃO, não na identidade — o índice continua fiel ao disco.
        var r = Com("Direito/Licitações.md");
        Assert.Equal("Direito/Licitações.md", r.Resolver("licitações")!.Valor);
    }
}
