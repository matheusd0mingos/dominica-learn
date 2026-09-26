using Dominica.Learn.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Infrastructure.Tests;

/// <summary>
/// O relógio do produto carrega o fuso do CONCURSEIRO, não o do servidor. O defeito que este teste
/// impede de voltar: com o relógio em UTC, quem estudava às 21h de Brasília tinha o dia virado — nota
/// diária de amanhã, heatmap no dia errado, cartão de amanhã vencendo à noite.
/// </summary>
public class RelogioDoSistemaTests
{
    private static RelogioDoSistema Montar(string? fuso)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(fuso is null
                ? []
                : new Dictionary<string, string?> { [RelogioDoSistema.ChaveDoFuso] = fuso })
            .Build();
        return new RelogioDoSistema(config, NullLogger<RelogioDoSistema>.Instance);
    }

    [Fact]
    public void O_padrao_e_o_fuso_de_Brasilia()
    {
        var relogio = Montar(fuso: null);

        // Brasil aboliu o horário de verão em 2019: o deslocamento é -03 o ano inteiro.
        Assert.Equal(TimeSpan.FromHours(-3), relogio.Agora.Offset);
    }

    [Fact]
    public void As_21h30_de_Brasilia_ainda_e_HOJE_mesmo_com_o_servidor_em_UTC()
    {
        var relogio = Montar(fuso: null);

        // 2026-08-08 21:30 em Brasília = 2026-08-09 00:30 UTC. A data do produto tem de ser a de quem
        // estuda — dia 8, não dia 9. É a diferença entre o heatmap dizer a verdade ou não.
        var utc = new DateTimeOffset(2026, 8, 9, 0, 30, 0, TimeSpan.Zero);
        var local = TimeZoneInfo.ConvertTime(utc, relogio.Fuso);

        Assert.Equal(new DateOnly(2026, 8, 8), DateOnly.FromDateTime(local.DateTime));
        Assert.Equal(new TimeOnly(21, 30), TimeOnly.FromDateTime(local.DateTime));
    }

    [Fact]
    public void Fuso_configurado_vale_e_fuso_invalido_degrada_para_UTC_sem_derrubar()
    {
        Assert.Equal(TimeSpan.Zero, Montar("Etc/UTC").Agora.Offset);
        Assert.Equal(TimeZoneInfo.Utc, Montar("Fuso/Inexistente").Fuso);
    }
}
