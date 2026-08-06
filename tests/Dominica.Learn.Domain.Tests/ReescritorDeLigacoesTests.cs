using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

// Renomear é a operação que mais assusta num vault: se ela quebrar as ligações de entrada, destrói
// justamente a rede que É o conhecimento. Cada teste aqui trava uma coisa que o reescritor preserva.
public class ReescritorDeLigacoesTests
{
    private static readonly CaminhoNota De = CaminhoNota.De("Direito/Licitações.md");
    private static readonly CaminhoNota Para = CaminhoNota.De("Direito/Contratos.md");

    private static string Reescrever(string texto) => ReescritorDeLigacoes.Reescrever(texto, De, Para);

    [Fact]
    public void Reescreve_wikilink_pelo_nome()
    {
        Assert.Equal("ver [[Contratos]] hoje", Reescrever("ver [[Licitações]] hoje"));
    }

    [Fact]
    public void Reescreve_wikilink_pelo_caminho()
    {
        Assert.Equal("[[Direito/Contratos]]", Reescrever("[[Direito/Licitações]]"));
    }

    [Fact]
    public void Preserva_o_rotulo()
    {
        // O rótulo é prosa do autor no meio de uma frase. Trocá-lo seria reescrever o texto dele.
        Assert.Equal("[[Contratos|as regras de 2021]]", Reescrever("[[Licitações|as regras de 2021]]"));
    }

    [Fact]
    public void Preserva_a_secao()
    {
        Assert.Equal("[[Contratos#Modalidades]]", Reescrever("[[Licitações#Modalidades]]"));
        Assert.Equal("[[Contratos#Modalidades|veja]]", Reescrever("[[Licitações#Modalidades|veja]]"));
    }

    [Fact]
    public void Preserva_a_altura_de_quem_escreveu()
    {
        // Quem escreveu só o nome continua com só o nome; trocar por caminho completo encheria o texto
        // de barras onde havia uma palavra.
        var alvoLongo = CaminhoNota.De("Concursos/Direito/Administrativo/Contratos.md");
        Assert.Equal("[[Contratos]]", ReescritorDeLigacoes.Reescrever("[[Licitações]]", De, alvoLongo));
        Assert.Equal("[[Concursos/Direito/Administrativo/Contratos]]",
            ReescritorDeLigacoes.Reescrever("[[Direito/Licitações]]", De, alvoLongo));
    }

    [Fact]
    public void Reescreve_embed()
    {
        Assert.Equal("![[Contratos]]", Reescrever("![[Licitações]]"));
    }

    [Fact]
    public void Reescreve_link_markdown_preservando_o_rotulo()
    {
        Assert.Equal("veja [as regras](Direito/Contratos.md)", Reescrever("veja [as regras](Direito/Licitações.md)"));
    }

    [Fact]
    public void Escapa_espaco_em_link_markdown()
    {
        var alvoComEspaco = CaminhoNota.De("Direito/Novos Contratos.md");
        Assert.Equal("[x](Direito/Novos%20Contratos.md)",
            ReescritorDeLigacoes.Reescrever("[x](Direito/Licitações.md)", De, alvoComEspaco));
    }

    [Fact]
    public void Nao_toca_em_ligacao_para_outra_nota()
    {
        const string texto = "[[Outra]] e [[Direito/Princípios]] e [externo](https://x.com)";
        Assert.Same(texto, Reescrever(texto));
    }

    [Fact]
    public void Devolve_a_mesma_instancia_quando_nada_muda()
    {
        // O chamador usa a identidade para decidir se grava. Sem isso, renomear reescreveria (e
        // arquivaria no histórico) todas as notas do vault, mudadas ou não.
        const string texto = "nada aqui aponta para lá";
        Assert.Same(texto, Reescrever(texto));
    }

    [Fact]
    public void Reescreve_varias_ocorrencias_na_mesma_nota()
    {
        Assert.Equal("[[Contratos]] e depois [[Contratos|de novo]] e [[Direito/Contratos]]",
            Reescrever("[[Licitações]] e depois [[Licitações|de novo]] e [[Direito/Licitações]]"));
    }

    [Fact]
    public void Colchete_solto_nao_quebra_o_texto()
    {
        const string texto = "um [ solto e um [[Licitações]] de verdade";
        Assert.Equal("um [ solto e um [[Contratos]] de verdade", Reescrever(texto));
    }

    [Fact]
    public void Texto_vazio_nao_quebra()
    {
        Assert.Equal(string.Empty, Reescrever(string.Empty));
    }
}
