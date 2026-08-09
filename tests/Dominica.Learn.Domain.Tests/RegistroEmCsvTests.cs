using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O CSV do registro é o que a pessoa abre na planilha para conferir a própria rotina — e é também o
/// formato do backup. Um campo mal escapado ou um instante no fuso errado não dá erro em lugar nenhum:
/// só produz uma planilha que mente.
/// </summary>
public class RegistroEmCsvTests
{
    private static readonly TimeZoneInfo Brasilia =
        TimeZoneInfo.CreateCustomTimeZone("teste-brasilia", TimeSpan.FromHours(-3), "Brasília", "Brasília");

    private static SessaoDeEstudo Sessao(string materia, DateTimeOffset inicio, int minutos, string? obs = null) =>
        SessaoDeEstudo.TentarCriar(Materia.De(materia), inicio, TimeSpan.FromMinutes(minutos), obs, out var s)
            ? s!
            : throw new InvalidOperationException("sessão de teste inválida");

    [Fact]
    public void SessaoSaiComCabecalhoEDados()
    {
        var linhas = RegistroEmCsv.Sessoes(
            [Sessao("Direito", new DateTimeOffset(2026, 8, 7, 14, 30, 0, TimeSpan.FromHours(-3)), 50, "Cronômetro")],
            Brasilia);

        Assert.Equal("inicio;materia;minutos;observacao", linhas[0]);
        Assert.Equal("2026-08-07 14:30;Direito;50;Cronômetro", linhas[1]);
    }

    [Fact]
    public void InstanteEmUtcSaiNoFusoDoConcurseiro()
    {
        // O DEFEITO QUE ESTE TESTE GUARDA: o banco fala UTC (ver InstanteParaUtc), e o CSV imprimia o UTC
        // cru — a sessão das 21h30 de Brasília aparecia no DIA SEGUINTE, à 00h30, numa planilha que
        // existe para conferir "quanto estudei em cada dia".
        var linhas = RegistroEmCsv.Sessoes(
            [Sessao("Direito", new DateTimeOffset(2026, 8, 8, 0, 30, 0, TimeSpan.Zero), 45)],
            Brasilia);

        Assert.StartsWith("2026-08-07 21:30;", linhas[1]);
    }

    [Fact]
    public void ObservacaoComPontoEVirgulaGanhaAspas()
    {
        // Ponto e vírgula é o SEPARADOR — sem aspas, "revisei ITCMD; ITBI" viraria duas colunas e
        // empurraria o resto da linha para o lado, silenciosamente.
        var linhas = RegistroEmCsv.Sessoes(
            [Sessao("Direito", new DateTimeOffset(2026, 8, 7, 14, 0, 0, TimeSpan.FromHours(-3)), 30,
                "revisei ITCMD; ITBI com \"pegadinha\"")],
            Brasilia);

        Assert.EndsWith(";30;\"revisei ITCMD; ITBI com \"\"pegadinha\"\"\"", linhas[1]);
    }

    [Fact]
    public void QuestoesSaemComCabecalhoEDados()
    {
        LoteDeQuestoes.TentarCriar(
            Materia.De("Português"), new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.FromHours(-3)),
            40, 27, TimeSpan.FromMinutes(50), "QConcursos", out var lote, out _);

        var linhas = RegistroEmCsv.Questoes([lote!], Brasilia);

        Assert.Equal("data;materia;questoes;acertos;segundos;fonte", linhas[0]);
        Assert.Equal("2026-08-07 09:00;Português;40;27;3000;QConcursos", linhas[1]);
    }

    [Fact]
    public void SemRegistroSaiSoOCabecalho()
    {
        // Cabeçalho sozinho, e não lista vazia: quem baixa o CSV de um registro vazio recebe um arquivo
        // que abre e mostra as colunas — não um arquivo de zero bytes que parece download quebrado.
        Assert.Single(RegistroEmCsv.Sessoes([], Brasilia));
        Assert.Single(RegistroEmCsv.Questoes([], Brasilia));
    }
}
