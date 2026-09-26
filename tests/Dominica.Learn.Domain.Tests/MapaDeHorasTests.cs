using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O histórico de horas responde "meu plano é real?" — semana, média e sequência. O risco mora nas bordas:
/// o fuso que muda o dia, a fronteira da semana (começa na segunda), e a sequência que um dia em branco
/// pela manhã não pode zerar antes do dia acabar.
/// </summary>
public class MapaDeHorasTests
{
    // Uma QUARTA-FEIRA, de propósito: a semana (segunda 03 → hoje 05) tem começo e fim claros, e o fim de
    // semana anterior (01 e 02) fica de fora — é a borda que mais engana.
    private static readonly DateOnly Hoje = new(2026, 8, 5);
    private static readonly Materia Direito = Materia.De("Direito");

    // Meio-dia com deslocamento zero, como no MapaDeCalor: em UTC o dia local não escorrega para o vizinho.
    private static SessaoDeEstudo Em(int diasAtras, int minutos) =>
        SessaoDeEstudo.TentarCriar(
            Direito,
            new DateTimeOffset(Hoje.AddDays(-diasAtras).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            TimeSpan.FromMinutes(minutos), null, out var s)
            ? s!
            : throw new InvalidOperationException("sessão de teste inválida");

    [Fact]
    public void A_grade_tem_sempre_semanas_x_7_dias_terminando_hoje()
    {
        var h = MapaDeHoras.Montar([], Hoje, semanas: 8);

        Assert.Equal(56, h.Dias.Count);
        Assert.Equal(Hoje, h.Dias[^1].Dia);
        Assert.Equal(Hoje.AddDays(-55), h.Dias[0].Dia);
        Assert.All(h.Dias, d => Assert.Equal(TimeSpan.Zero, d.Estudado));
        Assert.True(h.Vazio);
    }

    [Fact]
    public void Cada_dia_soma_a_duracao_DELE()
    {
        // Duas sessões hoje (50 + 30) e uma anteontem (25): o dia soma o que é dele.
        var h = MapaDeHoras.Montar([Em(0, 50), Em(0, 30), Em(2, 25)], Hoje);

        Assert.Equal(TimeSpan.FromMinutes(80), h.Dias[^1].Estudado);
        Assert.Equal(TimeSpan.FromMinutes(25), h.Dias[^3].Estudado);
        Assert.Equal(TimeSpan.Zero, h.Dias[^2].Estudado);
    }

    [Fact]
    public void Sessao_mais_velha_que_a_janela_fica_fora_do_total()
    {
        var h = MapaDeHoras.Montar([Em(200, 60)], Hoje, semanas: 8);
        Assert.True(h.Vazio);
        Assert.Equal(TimeSpan.Zero, h.Total);
    }

    [Fact]
    public void O_total_soma_todas_as_sessoes_da_janela()
    {
        var h = MapaDeHoras.Montar([Em(0, 50), Em(1, 40), Em(10, 30)], Hoje);
        Assert.Equal(TimeSpan.FromMinutes(120), h.Total);
    }

    [Fact]
    public void Nesta_semana_conta_da_segunda_ate_hoje_e_ignora_o_fim_de_semana_anterior()
    {
        // Hoje = quarta. Segunda(-2) e terça(-1) contam; domingo(-3) e sábado(-4) NÃO — são da semana passada.
        var h = MapaDeHoras.Montar([Em(0, 30), Em(1, 30), Em(2, 30), Em(3, 60), Em(4, 60)], Hoje);
        Assert.Equal(TimeSpan.FromMinutes(90), h.NestaSemana);   // qua + ter + seg
    }

    [Fact]
    public void A_media_e_por_dia_ESTUDADO_e_nao_por_dia_corrido()
    {
        // 60 + 20 em dois dias, com um dia de folga no meio: a média é 40 min (80/2), não 80/3. Descanso
        // não deve fazer parecer que você rende menos quando senta para estudar.
        var h = MapaDeHoras.Montar([Em(0, 60), Em(2, 20)], Hoje);
        Assert.Equal(2, h.DiasEstudados);
        Assert.Equal(TimeSpan.FromMinutes(40), h.MediaPorDia);
    }

    [Fact]
    public void A_sequencia_conta_dias_seguidos_terminando_hoje()
    {
        var h = MapaDeHoras.Montar([Em(0, 30), Em(1, 30), Em(2, 30), Em(4, 30)], Hoje);
        Assert.Equal(3, h.Sequencia);   // hoje, ontem, anteontem; o buraco no dia -3 corta
    }

    [Fact]
    public void Hoje_ainda_em_branco_NAO_zera_a_sequencia_conta_a_partir_de_ontem()
    {
        // De manhã, sem ter estudado hoje ainda: a sequência de ontem-e-antes segue de pé.
        var h = MapaDeHoras.Montar([Em(1, 30), Em(2, 30)], Hoje);
        Assert.Equal(2, h.Sequencia);
    }

    [Fact]
    public void Sem_estudo_ontem_nem_hoje_a_sequencia_e_zero()
    {
        var h = MapaDeHoras.Montar([Em(3, 30), Em(4, 30)], Hoje);
        Assert.Equal(0, h.Sequencia);
    }
}
