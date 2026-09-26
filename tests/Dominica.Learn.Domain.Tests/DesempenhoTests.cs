using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O acerto por matéria é o número que responde "onde estou perdendo desempenho", e é a partir dele que
/// a pessoa decide o que estudar. Um erro aqui não trava nada — muda a decisão do dia, em silêncio.
/// </summary>
public class DesempenhoTests
{
    private static readonly DateTimeOffset Em = new(2026, 8, 7, 14, 0, 0, TimeSpan.Zero);

    private static LoteDeQuestoes Lote(string materia, int total, int acertos, string fonte = "QConcursos") =>
        LoteDeQuestoes.TentarCriar(Materia.De(materia), Em, total, acertos, TimeSpan.Zero, fonte, out var l, out var p)
            ? l!
            : throw new InvalidOperationException($"lote de teste inválido: {p}");

    private static SessaoDeEstudo Sessao(string materia, int minutos) =>
        SessaoDeEstudo.TentarCriar(Materia.De(materia), Em, TimeSpan.FromMinutes(minutos), null, out var s)
            ? s!
            : throw new InvalidOperationException("sessão de teste inválida");

    private static DesempenhoDaMateria Achar(ResumoDeDesempenho r, string materia) =>
        r.Materias.Single(m => m.Materia.Nome == materia);

    // —— A ARMADILHA DA MÉDIA ————————————————————————————————————————————————————————

    [Fact]
    public void NAOEhMediaDeMedias()
    {
        // O TESTE QUE JUSTIFICA A CLASSE.
        //   10 questões a 90% →  9 acertos
        //  100 questões a 40% → 40 acertos
        //   média das médias:  (90+40)/2   = 65,0%
        //   acerto de verdade: (9+40)/110  = 44,5%
        // Vinte pontos para o lado otimista, justamente na matéria em que mais se errou.
        var r = CalculoDeDesempenho.Montar([Lote("Direito", 10, 9), Lote("Direito", 100, 40)], []);

        Assert.Equal(44.5, Achar(r, "Direito").Percentual, precision: 1);
    }

    [Fact]
    public void OTotalGERALTambemSomaAntesDeDividir()
    {
        var r = CalculoDeDesempenho.Montar([Lote("Direito", 10, 9), Lote("Português", 100, 40)], []);

        Assert.Equal(110, r.Questoes);
        Assert.Equal(49, r.Acertos);
        Assert.Equal(44.5, r.Percentual, precision: 1);
    }

    // —— AMOSTRA ————————————————————————————————————————————————————————————————————

    [Fact]
    public void PoucasQuestoesNaoSustentamConclusao()
    {
        // "50% de acerto" em duas questões é um número que existe e não significa nada. Deixá-lo
        // competir por atenção com uma matéria de 200 questões convida a uma decisão baseada em nada.
        var r = CalculoDeDesempenho.Montar([Lote("Direito", 2, 1)], []);

        Assert.False(Achar(r, "Direito").TemAmostraSuficiente);
    }

    [Fact]
    public void OPiorSOOlhaQuemTemAmostra()
    {
        // Direito tem 0% em duas questões; Português tem 40% em cem. O pior de verdade é Português.
        var r = CalculoDeDesempenho.Montar([Lote("Direito", 2, 0), Lote("Português", 100, 40)], []);

        Assert.Equal("Português", r.Pior!.Materia.Nome);
    }

    [Fact]
    public void SemNinguemComAmostraOPiorEhNULO()
    {
        // Nulo é a resposta honesta: ainda não dá para dizer onde se perde desempenho.
        var r = CalculoDeDesempenho.Montar([Lote("Direito", 3, 1)], []);

        Assert.Null(r.Pior);
    }

    [Fact]
    public void AsMateriasComAmostraVEMPRIMEIRO()
    {
        var r = CalculoDeDesempenho.Montar([Lote("SemAmostra", 3, 0), Lote("ComAmostra", 100, 90)], []);

        Assert.Equal("ComAmostra", r.Materias[0].Materia.Nome);
    }

    // —— HORAS ——————————————————————————————————————————————————————————————————————

    [Fact]
    public void HorasSomamPorMateriaENoTotal()
    {
        var r = CalculoDeDesempenho.Montar([], [Sessao("Direito", 90), Sessao("Direito", 30), Sessao("Português", 60)]);

        Assert.Equal(TimeSpan.FromHours(2), Achar(r, "Direito").Horas);
        Assert.Equal(TimeSpan.FromHours(3), r.Horas);
    }

    [Fact]
    public void MateriaSoComHoraAPARECE()
    {
        // Ler duas horas sem fazer questão nenhuma é estudo. Se a matéria só existisse quando há
        // questões, o painel de horas mentiria por omissão.
        var r = CalculoDeDesempenho.Montar([], [Sessao("Português", 120)]);

        var p = Achar(r, "Português");
        Assert.Equal(TimeSpan.FromHours(2), p.Horas);
        Assert.Equal(0, p.Questoes);
    }

    // —— O QUE NEM CHEGA A SER GRAVADO ——————————————————————————————————————————————

    [Fact]
    public void AcertarMaisDoQueSeFezEhRECUSADO()
    {
        // Erro de digitação. Gravado, produziria acerto acima de 100% contaminando toda a média daquela
        // matéria — sem dar erro em lugar nenhum.
        Assert.False(LoteDeQuestoes.TentarCriar(
            Materia.De("Direito"), Em, total: 10, acertos: 11, TimeSpan.Zero, null, out var l, out var p));

        Assert.Null(l);
        Assert.Equal(ProblemaDoLote.AcertosMaiorQueOTotal, p);
    }

    [Fact]
    public void TotalZeroOuNegativoEhRecusado()
    {
        Assert.False(LoteDeQuestoes.TentarCriar(Materia.De("D"), Em, 0, 0, TimeSpan.Zero, null, out _, out var p1));
        Assert.Equal(ProblemaDoLote.TotalInvalido, p1);

        Assert.False(LoteDeQuestoes.TentarCriar(Materia.De("D"), Em, -5, 0, TimeSpan.Zero, null, out _, out var p2));
        Assert.Equal(ProblemaDoLote.TotalInvalido, p2);
    }

    [Fact]
    public void SemMateriaEhRecusado()
    {
        // O lote sem matéria não responde "onde estou perdendo" — que é a única pergunta que ele existe
        // para responder.
        Assert.False(LoteDeQuestoes.TentarCriar(Materia.Nenhuma, Em, 10, 5, TimeSpan.Zero, null, out _, out var p));

        Assert.Equal(ProblemaDoLote.SemMateria, p);
    }

    [Fact]
    public void AcertarZeroEhVALIDO()
    {
        // Zerar um lote é informação, e das mais úteis. Recusá-lo apagaria justamente o pior sinal.
        Assert.True(LoteDeQuestoes.TentarCriar(Materia.De("D"), Em, 10, 0, TimeSpan.Zero, null, out var l, out _));

        Assert.Equal(0, l!.Percentual);
    }

    [Fact]
    public void SessaoLongaDEMAISEhRecusada()
    {
        // O erro de digitação mais comum num campo de minutos é um zero a mais: "600" passaria batido
        // enquanto estraga a média da semana.
        Assert.False(SessaoDeEstudo.TentarCriar(Materia.De("D"), Em, TimeSpan.FromHours(13), null, out _));
        Assert.True(SessaoDeEstudo.TentarCriar(Materia.De("D"), Em, TimeSpan.FromHours(3), null, out _));
    }

    [Fact]
    public void SessaoDeDuracaoZeroEhRecusada()
    {
        Assert.False(SessaoDeEstudo.TentarCriar(Materia.De("D"), Em, TimeSpan.Zero, null, out _));
    }

    [Fact]
    public void RegistroVazioNaoQuebra()
    {
        var r = CalculoDeDesempenho.Montar([], []);

        Assert.Equal(0, r.Questoes);
        Assert.Equal(0, r.Percentual);
        Assert.Empty(r.Materias);
        Assert.Null(r.Pior);
    }
}
