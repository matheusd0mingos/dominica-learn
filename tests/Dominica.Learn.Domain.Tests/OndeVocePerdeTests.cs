using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O registro de estudo puxando a fila de revisão.
///
/// Até isto existir, as duas metades do sistema não se falavam: o painel dizia "onde você mais perde:
/// Tributário — 41%" e a fila continuava idêntica. A informação existia e não mudava nada.
///
/// O QUE ESTES TESTES PROTEGEM não é o recurso, é o LIMITE dele. Uma fila que se reordena por desempenho
/// é útil; uma que reordena por cima do atraso é um sistema de repetição espaçada quebrado, e o defeito
/// só apareceria meses depois, como esquecimento.
/// </summary>
public class OndeVocePerdeTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 7);
    private static readonly DateTimeOffset Em = new(2026, 8, 7, 14, 0, 0, TimeSpan.Zero);

    private static LoteDeQuestoes Lote(string materia, int total, int acertos) =>
        LoteDeQuestoes.TentarCriar(Materia.De(materia), Em, total, acertos, TimeSpan.Zero, null, out var l, out _)
            ? l! : throw new InvalidOperationException("lote inválido");

    private static ResumoDeDesempenho Desempenho(params LoteDeQuestoes[] lotes) =>
        CalculoDeDesempenho.Montar(lotes, []);

    /// <summary>Um cartão de uma matéria, vencendo em <paramref name="vence"/>.</summary>
    private static Cartao C(string materia, string nota, int linha, DateOnly vence) =>
        new(CaminhoNota.De($"{materia}/{nota}.md"), $"P{linha}", "R", linha, 1,
            new Agendamento(vence, 5, 250));

    private static IReadOnlyList<string> Materias(IEnumerable<Cartao> fila) =>
        fila.Select(c => Materia.De(c.Nota).Nome).ToList();

    // —— QUEM ENTRA NA CONTA ————————————————————————————————————————————————————————

    [Fact]
    public void SoEntraMateriaComAMOSTRAEEMATRITO()
    {
        var d = Desempenho(
            Lote("Tributário", 100, 41),     // amostra e atrito → entra
            Lote("Português", 100, 85),      // amostra, sem atrito → fora
            Lote("Penal", 3, 0));            // atrito aparente, sem amostra → fora

        Assert.Equal(["Tributário"], OndeVocePerde.Ordenadas(d).Select(m => m.Nome));
    }

    [Fact]
    public void SEMAMOSTRANaoReordenaNADA()
    {
        // 0% em duas questões não é sinal de nada. Deixá-lo mandar na fila entregaria a sessão inteira a
        // um acidente de duas questões difíceis.
        var d = Desempenho(Lote("Penal", 2, 0));

        Assert.Empty(OndeVocePerde.Ordenadas(d));
        Assert.Null(OndeVocePerde.Explicar(d));
    }

    [Fact]
    public void SemRegistroNENHUMNaoQuebraENaoOpina()
    {
        Assert.Empty(OndeVocePerde.Ordenadas(null));
        Assert.Empty(OndeVocePerde.Ordenadas(Desempenho()));
        Assert.Null(OndeVocePerde.Explicar(null));
    }

    [Fact]
    public void APIORVemPrimeiro()
    {
        var d = Desempenho(Lote("Tributário", 100, 55), Lote("Penal", 100, 30));

        Assert.Equal(["Penal", "Tributário"], OndeVocePerde.Ordenadas(d).Select(m => m.Nome));
    }

    [Fact]
    public void EmpateDesfeitoPelaMAIORAMOSTRA()
    {
        // Duas a 45%: a de 200 questões é a que se sabe mesmo.
        var d = Desempenho(Lote("Pouca", 20, 9), Lote("Muita", 200, 90));

        Assert.Equal(["Muita", "Pouca"], OndeVocePerde.Ordenadas(d).Select(m => m.Nome));
    }

    // —— O LIMITE: O ATRASO NÃO SE NEGOCIA ——————————————————————————————————————————

    [Fact]
    public void CARTAOMAISATRASADOVemANTES_aindaQueDeMateriaBOA()
    {
        // O TESTE QUE JUSTIFICA A CLASSE INTEIRA. Português vai bem, Tributário vai mal — mas o cartão de
        // Português está vencido há uma semana. Ele vem primeiro. Reordenar por cima do atraso seria
        // deixar o esquecimento acontecer de propósito, que é o oposto do que o sistema existe para fazer.
        var d = Desempenho(Lote("Tributário", 100, 41), Lote("Português", 100, 90));

        var fila = OrdemDaFila.Intercalar(
            [C("Tributário", "a", 0, Hoje), C("Português", "b", 0, Hoje.AddDays(-7))],
            Hoje, OndeVocePerde.Prioridades(d));

        Assert.Equal(["Português", "Tributário"], Materias(fila));
    }

    [Fact]
    public void NOMESMODIA_aMateriaEmAtritoVemPrimeiro()
    {
        var d = Desempenho(Lote("Tributário", 100, 41), Lote("Português", 100, 90));

        var fila = OrdemDaFila.Intercalar(
            [C("Português", "b", 0, Hoje), C("Tributário", "a", 0, Hoje)],
            Hoje, OndeVocePerde.Prioridades(d));

        Assert.Equal(["Tributário", "Português"], Materias(fila));
    }

    [Fact]
    public void MateriaSEMDADONaoEhPUNIDA()
    {
        // Quem nunca registrou questão de Constitucional não vai para o fim da fila por isso. Só a
        // matéria MEDIDA E RUIM sobe; o resto fica onde estava, entre si na ordem de sempre.
        var d = Desempenho(Lote("Tributário", 100, 41));

        var fila = OrdemDaFila.Intercalar(
            [C("Constitucional", "a", 0, Hoje), C("Tributário", "b", 0, Hoje)],
            Hoje, OndeVocePerde.Prioridades(d));

        Assert.Equal(["Tributário", "Constitucional"], Materias(fila));
    }

    // —— O QUE NÃO PODE SE PERDER NO CAMINHO ————————————————————————————————————————

    [Fact]
    public void AINTERCALACAOPORNOTASOBREVIVE()
    {
        // A prioridade não pode desfazer o motivo pelo qual OrdemDaFila existe: dois cartões seguidos da
        // mesma nota fazem a pessoa lembrar do que leu três linhas acima, não do conceito.
        var d = Desempenho(Lote("Tributário", 100, 41));

        var fila = OrdemDaFila.Intercalar(
            [C("Tributário", "n1", 0, Hoje), C("Tributário", "n1", 1, Hoje),
             C("Tributário", "n2", 0, Hoje), C("Tributário", "n2", 1, Hoje)],
            Hoje, OndeVocePerde.Prioridades(d));

        var notas = fila.Select(c => c.Nota.Nome).ToList();
        Assert.Equal(["n1", "n2", "n1", "n2"], notas);
    }

    [Fact]
    public void SEMPRIORIDADE_aOrdemEhADESEMPRE()
    {
        // Compatibilidade explícita: com o registro vazio, a fila é byte a byte a mesma de antes deste
        // recurso existir. É o que garante que ligar isto não muda a rotina de quem não registra nada.
        Cartao[] cartoes = [C("B", "n1", 0, Hoje), C("A", "n2", 0, Hoje), C("B", "n1", 1, Hoje)];

        Assert.Equal(
            Materias(OrdemDaFila.Intercalar(cartoes, Hoje)),
            Materias(OrdemDaFila.Intercalar(cartoes, Hoje, OndeVocePerde.Prioridades(Desempenho()))));
    }

    [Fact]
    public void AOrdemEhESTAVEL()
    {
        // A fila é recalculada a cada resposta — o serviço não guarda sessão de propósito. Se a ordem
        // variasse entre dois cálculos iguais, a fila mudaria embaixo de quem está revisando.
        var d = Desempenho(Lote("Tributário", 100, 41));
        Cartao[] cartoes = [C("Tributário", "n1", 0, Hoje), C("Português", "n2", 0, Hoje)];

        Assert.Equal(
            Materias(OrdemDaFila.Intercalar(cartoes, Hoje, OndeVocePerde.Prioridades(d))),
            Materias(OrdemDaFila.Intercalar(cartoes, Hoje, OndeVocePerde.Prioridades(d))));
    }

    // —— A FRASE QUE APARECE NA TELA ————————————————————————————————————————————————

    [Fact]
    public void EXPLICADIZENDOONUMERO()
    {
        // Fila reordenada em silêncio é pior que fila não reordenada: a pessoa vê uma matéria que não
        // escolheu e conclui que o sistema está confuso, não que está ajudando.
        var texto = OndeVocePerde.Explicar(Desempenho(Lote("Tributário", 120, 49)));

        Assert.NotNull(texto);
        Assert.Contains("Tributário", texto);
        Assert.Contains("120 questões", texto);
        Assert.Contains("40.8%", texto.Replace(',', '.'));
    }
}
