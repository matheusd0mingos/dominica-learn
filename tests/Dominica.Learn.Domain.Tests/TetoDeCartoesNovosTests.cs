using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O teto existe para conter a dívida de revisão, que é o que mata uma rotina de estudo. Mas ele só
/// pode conter o que é NOVO: esconder revisão vencida seria deixar o esquecimento acontecer de
/// propósito. Quase todo teste aqui defende essa fronteira.
/// </summary>
public class TetoDeCartoesNovosTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 7);

    private static Cartao Inedito(string nota, int linha) =>
        new(CaminhoNota.De(nota), $"p{linha}", "r", linha, 1, null);

    private static Cartao Vencido(string nota, int linha, int intervalo = 6) =>
        new(CaminhoNota.De(nota), $"p{linha}", "r", linha, 1,
            new Agendamento(Hoje.AddDays(-1), intervalo, 250));

    /// <summary>Um cartão respondido hoje pela primeira vez: intervalo 1, vencendo amanhã.</summary>
    private static Cartao IntroduzidoHoje(string nota, int linha) =>
        new(CaminhoNota.De(nota), $"p{linha}", "r", linha, 1,
            new Agendamento(Hoje.AddDays(1), 1, 250));

    [Fact]
    public void OTetoCortaOsIneditosQuePassamDele()
    {
        var fila = new[] { Inedito("a.md", 1), Inedito("a.md", 2), Inedito("a.md", 3) };

        var comTeto = TetoDeCartoesNovos.Aplicar(fila, fila, Hoje, teto: 2);

        Assert.Equal(2, comTeto.Count);
    }

    [Fact]
    public void RevisaoVencidaNUNCAEhCortada()
    {
        // A FRONTEIRA QUE JUSTIFICA A CLASSE. Cartão já estudado que venceu tem de aparecer, todos eles —
        // esconder revisão vencida é deixar o esquecimento acontecer de propósito.
        var fila = new[] { Vencido("a.md", 1), Vencido("a.md", 2), Vencido("a.md", 3), Vencido("a.md", 4) };

        var comTeto = TetoDeCartoesNovos.Aplicar(fila, fila, Hoje, teto: 1);

        Assert.Equal(4, comTeto.Count);
    }

    [Fact]
    public void OTetoSoRacionaOsIneditosDaFilaMista()
    {
        var fila = new[]
        {
            Vencido("a.md", 1), Inedito("b.md", 1), Vencido("a.md", 2),
            Inedito("b.md", 2), Inedito("b.md", 3),
        };

        var comTeto = TetoDeCartoesNovos.Aplicar(fila, fila, Hoje, teto: 1);

        Assert.Equal(3, comTeto.Count);
        Assert.Single(comTeto, TetoDeCartoesNovos.EhInedito);
        Assert.Equal(2, comTeto.Count(c => !TetoDeCartoesNovos.EhInedito(c)));
    }

    [Fact]
    public void AOrdEmDaFilaEhPreservada()
    {
        // O teto corta, não reordena: a intercalação já decidiu a ordem, e refazê-la aqui desfaria o
        // trabalho de OrdemDaFila.
        var fila = new[] { Vencido("z.md", 1), Inedito("a.md", 1), Vencido("m.md", 1) };

        var comTeto = TetoDeCartoesNovos.Aplicar(fila, fila, Hoje, teto: 5);

        Assert.Equal(["z", "a", "m"], comTeto.Select(c => c.Nota.Nome).ToArray());
    }

    [Fact]
    public void OQueJaFoiIntroduzidoHojeConsomeACota()
    {
        // O cartão respondido hoje JÁ SAIU da fila e ainda assim custou esforço. Sem contá-lo, o teto
        // seria por sessão e não por dia: bastaria recarregar a página para ganhar mais vinte.
        var jaFeitos = new[] { IntroduzidoHoje("a.md", 1), IntroduzidoHoje("a.md", 2) };
        var fila = new[] { Inedito("b.md", 1), Inedito("b.md", 2), Inedito("b.md", 3) };

        var comTeto = TetoDeCartoesNovos.Aplicar(fila, [.. jaFeitos, .. fila], Hoje, teto: 3);

        Assert.Single(comTeto);
    }

    [Fact]
    public void CotaEstouradaNoDiaTiraTodosOsIneditos()
    {
        var jaFeitos = new[] { IntroduzidoHoje("a.md", 1), IntroduzidoHoje("a.md", 2) };
        var fila = new[] { Inedito("b.md", 1), Vencido("c.md", 1) };

        var comTeto = TetoDeCartoesNovos.Aplicar(fila, [.. jaFeitos, .. fila], Hoje, teto: 2);

        // Sobra só a revisão vencida.
        Assert.Single(comTeto);
        Assert.False(TetoDeCartoesNovos.EhInedito(comTeto[0]));
    }

    [Fact]
    public void TetoZeroSignificaSemTeto()
    {
        var fila = Enumerable.Range(1, 50).Select(i => Inedito("a.md", i)).ToArray();

        Assert.Equal(50, TetoDeCartoesNovos.Aplicar(fila, fila, Hoje, TetoDeCartoesNovos.SemTeto).Count);
    }

    [Fact]
    public void CartaoErradoHojeNaoContaComoIntroduzido()
    {
        // "Errei" devolve o cartão para o intervalo ZERO, vencendo hoje — ele continua na fila e não
        // consumiu cota nenhuma. Contá-lo puniria quem errou tirando os cartões novos do dia.
        var errado = new Cartao(CaminhoNota.De("a.md"), "p", "r", 1, 1, new Agendamento(Hoje, 0, 230));

        Assert.Equal(0, TetoDeCartoesNovos.IntroduzidosHoje([errado], Hoje));
    }

    [Fact]
    public void CartaoVeteranoRespondidoHojeNaoContaComoIntroduzido()
    {
        // Intervalo grande vencendo lá na frente é revisão de veterano, não estreia. Contá-lo faria um
        // dia de revisão pesada zerar a cota de cartões novos sem motivo.
        var veterano = new Cartao(CaminhoNota.De("a.md"), "p", "r", 1, 1,
            new Agendamento(Hoje.AddDays(15), 15, 250));

        Assert.Equal(0, TetoDeCartoesNovos.IntroduzidosHoje([veterano], Hoje));
    }

    [Fact]
    public void FilaVaziaNaoQuebra() =>
        Assert.Empty(TetoDeCartoesNovos.Aplicar([], [], Hoje, teto: 20));
}
