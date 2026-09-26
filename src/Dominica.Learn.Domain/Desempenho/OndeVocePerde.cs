using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Desempenho;

/// <summary>
/// As matérias em que o desempenho medido diz que se está perdendo — da pior para a menos pior.
///
/// ESTA CLASSE É A JUNÇÃO DAS DUAS METADES DO SISTEMA, e até ela existir elas não se falavam. De um lado,
/// a revisão espaçada, que ordena a fila pelo que os CARTÕES dizem: atraso e facilidade. Do outro, o
/// registro, que sabe onde a pessoa erra QUESTÃO DE PROVA. O painel mostrava "onde você mais perde:
/// Tributário — 41%" e a fila continuava exatamente a mesma. A informação existia e não mudava nada.
///
/// POR QUE OS DOIS SINAIS NÃO SÃO O MESMO: a facilidade do cartão mede o quanto você lembra do que
/// escreveu. O acerto em questões mede o quanto você acerta o que a banca pergunta. Dá para ter cartões
/// fáceis numa matéria em que se vai mal na prova — é o caso mais comum, e o mais perigoso, porque o
/// sistema só olhando cartões diria que está tudo bem.
///
/// O QUE ELA NÃO FAZ, e é deliberado:
///
///   - NÃO esconde revisão vencida. Cartão vencido aparece, todos eles, sempre. Mexer nisso seria
///     deixar o esquecimento acontecer de propósito. O que ela muda é a ORDEM de hoje e QUAIS inéditos
///     passam pelo teto — as duas decisões que já eram tomadas, e eram tomadas no escuro.
///   - NÃO promove matéria sem amostra. Zero por cento em duas questões não é sinal de nada, e deixá-lo
///     reordenar a fila entregaria a sessão a um acidente. Mesma guarda do painel.
///   - NÃO pune matéria sem dado nenhum. Quem nunca registrou questão de uma matéria não vai para o fim
///     da fila por isso — não se castiga o que não se mediu.
/// </summary>
public static class OndeVocePerde
{
    /// <summary>
    /// Abaixo disto a matéria é considerada em atrito. É o MESMO 60% que a tabela do painel já pinta de
    /// vermelho — dois números diferentes para a mesma ideia fariam a tela e a fila discordarem, e a
    /// pessoa não teria como saber qual das duas acreditar.
    /// </summary>
    public const double AcertoDeAtrito = 60.0;

    /// <summary>
    /// As matérias em atrito, da pior para a menos pior. Vazio quando não há amostra suficiente em
    /// lugar nenhum — que é a resposta honesta de um sistema que ainda não mediu nada.
    /// </summary>
    public static IReadOnlyList<Materia> Ordenadas(ResumoDeDesempenho? desempenho)
    {
        if (desempenho is null) return [];

        return desempenho.Materias
            .Where(m => m.TemAmostraSuficiente && m.Percentual < AcertoDeAtrito)
            .OrderBy(m => m.Percentual)
            // Empate desfeito pela MAIOR amostra: entre duas a 45%, a de 200 questões é a que se sabe
            // mesmo. E é estável — a mesma medição devolve sempre a mesma ordem.
            .ThenByDescending(m => m.Questoes)
            .ThenBy(m => m.Materia.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => m.Materia)
            .ToList();
    }

    /// <summary>
    /// A posição de uma matéria na ordem de prioridade: 0 para a pior, 1 para a seguinte, e
    /// <see cref="Neutra"/> para quem não está em atrito.
    ///
    /// Vira chave de ordenação — ver <see cref="OrdemDaFila"/> no projeto de cartões.
    /// </summary>
    public const int Neutra = int.MaxValue;

    public static IReadOnlyDictionary<Materia, int> Prioridades(ResumoDeDesempenho? desempenho)
    {
        var ordem = Ordenadas(desempenho);
        var mapa = new Dictionary<Materia, int>(ordem.Count);
        for (var i = 0; i < ordem.Count; i++) mapa[ordem[i]] = i;
        return mapa;
    }

    /// <summary>
    /// A frase que explica a ordem, para a tela. Nula quando não há nada a explicar.
    ///
    /// EXISTE PORQUE UMA FILA REORDENADA EM SILÊNCIO É PIOR QUE UMA FILA NÃO REORDENADA. A pessoa abre a
    /// revisão, vê uma matéria que não escolheu, e a conclusão dela é que o sistema está confuso — não
    /// que ele está ajudando. A regra vale para o painel inteiro: sugestão sem motivo é ordem, e ordem
    /// sem explicação deixa de ser obedecida na primeira vez que se discorda dela.
    /// </summary>
    public static string? Explicar(ResumoDeDesempenho? desempenho)
    {
        if (desempenho is null) return null;

        var pior = desempenho.Materias
            .Where(m => m.TemAmostraSuficiente && m.Percentual < AcertoDeAtrito)
            .OrderBy(m => m.Percentual)
            .FirstOrDefault();

        return pior is null
            ? null
            : $"{pior.Materia.Rotulo} vem primeiro: {pior.Percentual:0.#}% de acerto em " +
              $"{pior.Questoes} questões nas últimas semanas.";
    }
}
