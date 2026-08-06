using Dominica.Learn.Domain.Templates;

namespace Dominica.Learn.Domain.Tests;

public class AplicadorDeTemplateTests
{
    private static readonly ContextoDoTemplate Ctx =
        new("Licitações", new DateTimeOffset(2026, 8, 6, 15, 30, 0, TimeSpan.Zero), "Matheus");

    [Fact]
    public void Substitui_titulo_e_autor()
    {
        Assert.Equal("# Licitações — por Matheus",
            AplicadorDeTemplate.Aplicar("# {{titulo}} — por {{autor}}", Ctx));
    }

    [Fact]
    public void Data_e_hora_aceitam_formato()
    {
        Assert.Contains("2026-08-06", AplicadorDeTemplate.Aplicar("{{data}}", Ctx));
        Assert.Contains("06/08/2026", AplicadorDeTemplate.Aplicar("{{data:dd/MM/yyyy}}", Ctx));
    }

    [Fact]
    public void Variavel_desconhecida_e_deixada_como_esta()
    {
        // Ver "{{quest}}" no texto conta o que aconteceu; ver um buraco não conta nada.
        Assert.Equal("antes {{quest}} depois", AplicadorDeTemplate.Aplicar("antes {{quest}} depois", Ctx));
    }

    [Fact]
    public void Chaves_desbalanceadas_nao_quebram()
    {
        Assert.Equal("{{ sem fechar", AplicadorDeTemplate.Aplicar("{{ sem fechar", Ctx));
    }

    [Fact]
    public void Espaco_dentro_das_chaves_e_tolerado()
    {
        Assert.Equal("Licitações", AplicadorDeTemplate.Aplicar("{{  titulo  }}", Ctx));
    }

    [Fact]
    public void Template_sem_variavel_passa_intacto()
    {
        const string t = "# Roteiro\n\n- [ ] ler\n- [ ] resumir\n\n#direito";
        Assert.Equal(t, AplicadorDeTemplate.Aplicar(t, Ctx));
    }

    [Fact]
    public void Template_vazio_nao_quebra()
    {
        Assert.Equal(string.Empty, AplicadorDeTemplate.Aplicar(string.Empty, Ctx));
    }

    [Fact]
    public void Varias_ocorrencias_da_mesma_variavel()
    {
        Assert.Equal("Licitações e Licitações", AplicadorDeTemplate.Aplicar("{{titulo}} e {{titulo}}", Ctx));
    }
}
