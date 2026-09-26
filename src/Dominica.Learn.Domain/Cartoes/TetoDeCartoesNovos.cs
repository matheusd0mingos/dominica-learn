namespace Dominica.Learn.Domain.Cartoes;

/// <summary>
/// Quantos cartões INÉDITOS podem entrar na revisão por dia.
///
/// POR QUE ESTE É O PARÂMETRO QUE MAIS IMPORTA numa rotina de estudo:
///
/// Escrever uma matéria nova produz dezenas de cartões de uma vez — a de Contabilidade Avançada
/// produziu 37. Todos vencem no mesmo dia. Sem teto, a pessoa responde os 37, e amanhã eles voltam
/// somados aos novos que ela escrever amanhã. Em duas semanas a fila tem centenas de cartões e a sessão
/// diária passa de vinte minutos para duas horas — e aí ela para de abrir o app. A dívida de revisão não
/// avisa que está crescendo: ela só aparece no dia em que já é impagável.
///
/// O TETO NÃO VALE PARA REVISÃO ATRASADA, e essa distinção é a alma da coisa. Cartão que já foi
/// estudado e venceu tem de aparecer, todos eles: esconder revisão vencida é deixar o esquecimento
/// acontecer de propósito, que é o oposto do que o sistema existe para fazer. O teto racionha só o que
/// é NOVO — o que ainda não custou nada e pode esperar até amanhã sem perda nenhuma.
/// </summary>
public static class TetoDeCartoesNovos
{
    /// <summary>Vinte por dia. É o padrão do Anki, e é aproximadamente o que cabe numa sessão diária real.</summary>
    public const int Padrao = 20;

    /// <summary>Zero significa SEM TETO — para quem quer o comportamento antigo de volta.</summary>
    public const int SemTeto = 0;

    /// <summary>Um cartão inédito é o que nunca foi respondido: nenhuma marca de agendamento no arquivo.</summary>
    public static bool EhInedito(Cartao cartao) => cartao.Agendamento is null;

    /// <summary>
    /// Quantos cartões inéditos já entraram no ciclo hoje.
    ///
    /// SAI DOS PRÓPRIOS ARQUIVOS, sem estado novo em lugar nenhum — o que é o que permite que a conta
    /// sobreviva a um "reindexar do zero" e continue certa se a pessoa revisar pelo Obsidian. Um cartão
    /// inédito respondido hoje sai do intervalo 0 para o intervalo 1, vencendo amanhã; é essa a
    /// assinatura procurada aqui.
    ///
    /// O QUE ELA CONTA A MAIS, dito às claras: um cartão que já estava no intervalo 1 e foi respondido
    /// hoje como "difícil" permanece no intervalo 1 e entra nesta conta sem ser inédito. É raro, e o
    /// efeito é mostrar UM cartão novo a menos hoje — erra para o lado de dar menos trabalho, que é o
    /// lado certo para errar num teto que existe justamente para conter trabalho.
    /// </summary>
    public static int IntroduzidosHoje(IEnumerable<Cartao> todos, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(todos);

        var amanha = hoje.AddDays(1);
        return todos.Count(c => c.Agendamento is { IntervaloEmDias: 1 } a && a.Vence == amanha);
    }

    /// <summary>
    /// A fila com o teto aplicado. <paramref name="fila"/> já vem na ordem final (ver
    /// <see cref="OrdemDaFila"/>); <paramref name="todos"/> é o vault inteiro, porque o cartão
    /// respondido hoje já saiu da fila e ainda assim consumiu a cota.
    /// </summary>
    public static IReadOnlyList<Cartao> Aplicar(
        IReadOnlyList<Cartao> fila, IEnumerable<Cartao> todos, DateOnly hoje, int teto)
    {
        ArgumentNullException.ThrowIfNull(fila);

        if (teto <= SemTeto) return fila;

        var restam = Math.Max(0, teto - IntroduzidosHoje(todos, hoje));

        var escolhidos = new List<Cartao>(fila.Count);
        foreach (var cartao in fila)
        {
            if (EhInedito(cartao))
            {
                if (restam == 0) continue;
                restam--;
            }
            escolhidos.Add(cartao);
        }

        return escolhidos;
    }
}
