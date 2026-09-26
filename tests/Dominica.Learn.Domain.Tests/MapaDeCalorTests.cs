using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O mapa de calor responde "estudei todo dia?" — e a resposta errada aqui mente sobre a rotina de
/// alguém. O risco é de borda: fuso, janela, dia vazio.
/// </summary>
public class MapaDeCalorTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 8);
    private static readonly Materia Direito = Materia.De("Direito");

    private static RevisaoDeCartao Em(int diasAtras, Resposta resposta = Resposta.Bom) =>
        new(new DateTimeOffset(Hoje.AddDays(-diasAtras).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            Direito, resposta);

    [Fact]
    public void A_grade_tem_sempre_semanas_x_7_dias_terminando_hoje()
    {
        var dias = MapaDeCalor.Montar([], Hoje, semanas: 8);

        Assert.Equal(56, dias.Count);
        Assert.Equal(Hoje, dias[^1].Dia);
        Assert.Equal(Hoje.AddDays(-55), dias[0].Dia);
        Assert.All(dias, d => Assert.Equal(0, d.Revisoes));
    }

    [Fact]
    public void Cada_dia_conta_as_revisoes_DELE()
    {
        var dias = MapaDeCalor.Montar([Em(0), Em(0), Em(3)], Hoje);

        Assert.Equal(2, dias[^1].Revisoes);
        Assert.Equal(1, dias[^4].Revisoes);
        Assert.Equal(0, dias[^2].Revisoes);
    }

    [Fact]
    public void Revisao_mais_velha_que_a_janela_fica_fora()
    {
        var dias = MapaDeCalor.Montar([Em(200)], Hoje, semanas: 8);
        Assert.All(dias, d => Assert.Equal(0, d.Revisoes));
    }

    [Fact]
    public void Por_cento_lembrado_e_tudo_que_nao_foi_Errei()
    {
        var revisoes = new[] { Em(0), Em(0, Resposta.Errei), Em(1, Resposta.Facil), Em(2, Resposta.Dificil) };
        Assert.Equal(75, MapaDeCalor.PorCentoLembrado(revisoes));
    }

    [Fact]
    public void Sem_revisao_nenhuma_o_lembrado_e_zero_e_nao_divide_por_zero()
    {
        Assert.Equal(0, MapaDeCalor.PorCentoLembrado([]));
    }
}
