using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Desempenho;

/// <summary>O desempenho de uma matéria no período.</summary>
public sealed record DesempenhoDaMateria(
    Materia Materia,
    int Questoes,
    int Acertos,
    TimeSpan Horas,
    /// <summary>Lotes registrados. Poucos lotes com poucas questões não sustentam conclusão nenhuma.</summary>
    int Lotes)
{
    /// <summary>Acerto de 0 a 100. Ver <see cref="ResumoDeDesempenho"/> para por que ele NÃO é média de médias.</summary>
    public double Percentual => Questoes == 0 ? 0 : Acertos * 100.0 / Questoes;

    /// <summary>
    /// Abaixo disto, o número existe e não significa nada. Vinte questões é pouco para julgar uma
    /// matéria, e mostrar "50%" para duas questões convida a uma decisão baseada em nada.
    /// </summary>
    public const int MinimoParaConcluir = 20;

    public bool TemAmostraSuficiente => Questoes >= MinimoParaConcluir;
}

/// <summary>Tudo que o registro de desempenho responde num período.</summary>
public sealed record ResumoDeDesempenho(
    int Questoes,
    int Acertos,
    TimeSpan Horas,
    IReadOnlyList<DesempenhoDaMateria> Materias)
{
    public static readonly ResumoDeDesempenho Vazio = new(0, 0, TimeSpan.Zero, []);

    public double Percentual => Questoes == 0 ? 0 : Acertos * 100.0 / Questoes;

    /// <summary>
    /// A matéria com pior acerto entre as que têm amostra suficiente. Null quando ninguém tem.
    ///
    /// É a resposta a "onde estou perdendo desempenho" — a pergunta que o produto promete e que só o
    /// registro de questões consegue responder de verdade.
    /// </summary>
    public DesempenhoDaMateria? Pior =>
        Materias.Where(m => m.TemAmostraSuficiente).OrderBy(m => m.Percentual).FirstOrDefault();
}

/// <summary>
/// Agrega o registro de estudo num resumo. Pura.
///
/// A ARMADILHA QUE ESTA CLASSE EXISTE PARA EVITAR é a MÉDIA DE MÉDIAS. Somar os percentuais dos lotes e
/// dividir pela quantidade parece a mesma conta e não é:
///
///   • 10 questões com 90% de acerto  → 9 acertos
///   • 100 questões com 40% de acerto → 40 acertos
///
///   média das médias:  (90 + 40) / 2      = 65%
///   acerto de verdade: (9 + 40) / 110     = 44,5%
///
/// Vinte pontos de diferença, para o lado otimista, exatamente na matéria em que a pessoa mais errou —
/// porque o lote pequeno e bem-sucedido pesa igual ao lote grande e ruim. O painel diria que está tudo
/// bem justamente onde não está, e a decisão do dia sairia errada sem ninguém perceber.
///
/// Aqui somam-se ACERTOS e QUESTÕES, e a divisão é feita uma vez, no fim.
/// </summary>
// O nome NÃO pode ser "Desempenho": é o do namespace, e C# resolve o namespace primeiro. É a SEGUNDA vez
// que isto acontece neste projeto — a primeira foi PainelDeEstudo. A regra prática: classe estática que
// dá nome a um assunto nunca leva o nome da pasta em que mora.
public static class CalculoDeDesempenho
{
    public static ResumoDeDesempenho Montar(
        IEnumerable<LoteDeQuestoes> lotes, IEnumerable<SessaoDeEstudo> sessoes)
    {
        ArgumentNullException.ThrowIfNull(lotes);
        ArgumentNullException.ThrowIfNull(sessoes);

        var porLote = lotes.ToList();
        var porSessao = sessoes.ToList();

        var materias = porLote.Select(l => l.Materia)
            .Concat(porSessao.Select(s => s.Materia))
            .Distinct()
            .Select(m =>
            {
                var seus = porLote.Where(l => l.Materia == m).ToList();
                return new DesempenhoDaMateria(
                    m,
                    // SOMA de questões e de acertos, nunca média de percentuais. Ver o comentário da classe.
                    seus.Sum(l => l.Total),
                    seus.Sum(l => l.Acertos),
                    new TimeSpan(porSessao.Where(s => s.Materia == m).Sum(s => s.Duracao.Ticks)),
                    seus.Count);
            })
            // Pior acerto primeiro entre as que têm amostra; as sem amostra vão para o fim, porque não
            // sustentam conclusão nenhuma e não devem competir por atenção com as que sustentam.
            .OrderBy(m => m.TemAmostraSuficiente ? 0 : 1)
            .ThenBy(m => m.TemAmostraSuficiente ? m.Percentual : 0)
            .ThenByDescending(m => m.Questoes)
            .ThenBy(m => m.Materia.Rotulo, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new ResumoDeDesempenho(
            porLote.Sum(l => l.Total),
            porLote.Sum(l => l.Acertos),
            new TimeSpan(porSessao.Sum(s => s.Duracao.Ticks)),
            materias);
    }
}
