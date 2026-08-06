using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

public class CaminhoNotaTests
{
    [Theory]
    [InlineData("Nota", "Nota.md")]
    [InlineData("Nota.md", "Nota.md")]
    [InlineData("Direito\\Licitações.md", "Direito/Licitações.md")]   // barra do Windows normalizada
    [InlineData("/Direito/Licitações", "Direito/Licitações.md")]      // barra inicial removida
    [InlineData("Direito//Licitações", "Direito/Licitações.md")]      // barra dupla colapsada
    [InlineData("  Nota  ", "Nota.md")]
    public void Normaliza_a_entrada(string bruto, string esperado)
    {
        Assert.Equal(esperado, CaminhoNota.De(bruto).Valor);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("Direito/../../fora")]
    [InlineData("./Nota")]
    [InlineData("C:/Windows/system.ini")]
    [InlineData("Nota<ruim>")]
    [InlineData("Nota|pipe")]
    [InlineData("")]
    [InlineData("   ")]
    public void Recusa_caminho_que_escapa_do_vault_ou_e_ilegal(string bruto)
    {
        // A recusa mora no DOMÍNIO, não só no adaptador de disco: quem sabe que caminho de nota é sempre
        // relativo e sempre para dentro é o domínio. O adaptador confere de novo, por profundidade.
        Assert.False(CaminhoNota.TentarCriar(bruto, out var c, out var erro));
        Assert.Null(c);
        Assert.NotNull(erro);
    }

    [Fact]
    public void Nome_pasta_e_segmentos()
    {
        var c = CaminhoNota.De("Concursos/Direito/Licitações.md");
        Assert.Equal("Licitações", c.Nome);
        Assert.Equal("Concursos/Direito", c.Pasta);
        Assert.Equal(["Concursos", "Direito"], c.Segmentos);
    }

    [Fact]
    public void Nota_na_raiz_tem_pasta_vazia()
    {
        var c = CaminhoNota.De("Índice.md");
        Assert.Equal(string.Empty, c.Pasta);
        Assert.Empty(c.Segmentos);
    }

    [Fact]
    public void Renomear_e_mover_preservam_o_resto()
    {
        var c = CaminhoNota.De("Direito/Licitações.md");
        Assert.Equal("Direito/Contratos.md", c.Renomear("Contratos").Valor);
        Assert.Equal("Arquivo/Licitações.md", c.MoverPara("Arquivo").Valor);
        Assert.Equal("Licitações.md", c.MoverPara(string.Empty).Valor);
    }

    [Fact]
    public void Comparacao_e_sensivel_a_maiusculas()
    {
        // O Linux distingue os dois arquivos. Tratá-los como a mesma nota faria o índice divergir do disco
        // no primeiro par que diferisse só na caixa — e o vault é a fonte da verdade.
        Assert.NotEqual(CaminhoNota.De("Nota.md"), CaminhoNota.De("nota.md"));
    }

    [Fact]
    public void Extensao_e_reconhecida_sem_diferenciar_caixa()
    {
        Assert.Equal("Nota.MD", CaminhoNota.De("Nota.MD").Valor);   // não duplica a extensão
    }
}
