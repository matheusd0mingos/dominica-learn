using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Painel;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// Prazo é a informação mais perigosa do vault: prazo lido errado é inscrição perdida. Por isso os
/// formatos são fechados (ISO e brasileiro) e o ambíguo é recusado, não adivinhado.
/// </summary>
public class PrazosTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 8);

    private static Frontmatter Fm(string valor) =>
        Frontmatter.Ler(["---", $"prazo: {valor}", "---"]);

    [Theory]
    [InlineData("2026-09-01")]
    [InlineData("01/09/2026")]
    public void Le_ISO_e_o_formato_brasileiro(string texto)
    {
        Assert.Equal(new DateOnly(2026, 9, 1), Prazos.De(Fm(texto)));
    }

    [Fact]
    public void Texto_que_nao_e_data_vira_nulo_e_nao_uma_data_adivinhada()
    {
        Assert.Null(Prazos.De(Fm("em breve")));
        Assert.Null(Prazos.De(Fm("09/2026")));
        Assert.Null(Prazos.De(Frontmatter.Ler(["---", "favorito: true", "---"])));
    }

    [Theory]
    [InlineData(0, "é hoje")]
    [InlineData(1, "amanhã")]
    [InlineData(5, "em 5 dias")]
    [InlineData(-1, "venceu ontem")]
    [InlineData(-3, "venceu há 3 dias")]
    public void O_rotulo_diz_a_distancia_em_portugues(int dias, string esperado)
    {
        Assert.Equal(esperado, Prazos.Rotulo(Hoje.AddDays(dias), Hoje));
    }

    [Fact]
    public void O_painel_poe_os_vencidos_primeiro_e_corta_o_alem_de_um_ano()
    {
        var prazos = new[]
        {
            new PrazoDaNota(CaminhoNota.De("Futuro.md"), Hoje.AddDays(30)),
            new PrazoDaNota(CaminhoNota.De("Vencido.md"), Hoje.AddDays(-2)),
            new PrazoDaNota(CaminhoNota.De("Longe.md"), Hoje.AddDays(400)),
        };

        var lista = Prazos.ParaOPainel(prazos, Hoje);

        Assert.Equal(2, lista.Count);
        Assert.Equal("Vencido.md", lista[0].Nota.Valor);
        Assert.Equal("Futuro.md", lista[1].Nota.Valor);
    }
}
