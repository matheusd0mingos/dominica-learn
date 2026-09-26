using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Cartoes;

/// <summary>
/// A ordem em que os cartões chegam na revisão.
///
/// O PROBLEMA QUE ELA RESOLVE apareceu usando, com uma matéria de verdade: ordenando por atraso e depois
/// por caminho, todos os cartões de uma nota vêm em bloco. Numa matéria recém-escrita, em que todos os
/// cartões são novos e vencem no mesmo dia, a sessão vira "três de CPC 18, quatro de CPC 36, três de
/// CPC 47" — foi exatamente o que saiu no teste.
///
/// Isso não é detalhe de apresentação, é o que a revisão faz ou deixa de fazer. Respondendo o quarto
/// cartão seguido da mesma nota, a pessoa não está lembrando do conceito: está lembrando do que acabou
/// de ler três linhas acima. O esforço de recuperar da memória — que é o mecanismo inteiro da repetição
/// espaçada — desaparece, e a sessão só produz a sensação de ter estudado.
///
/// A SOLUÇÃO NÃO PODE SER EMBARALHAR, e é por isso que existe uma classe em vez de um <c>OrderBy</c>
/// aleatório: a fila é recalculada a cada resposta (o serviço não guarda sessão, de propósito, para não
/// perder a revisão de quem fechou o navegador). Ordem aleatória mudaria a fila embaixo de quem está
/// revisando, a cada cartão respondido.
///
/// O QUE SE FAZ, ENTÃO: intercala em rodadas. O primeiro cartão de cada nota, depois o segundo de cada
/// nota, e assim por diante. É determinístico — o mesmo vault dá sempre a mesma fila — e separa os
/// cartões da mesma nota o máximo que a quantidade de notas permite.
///
/// O ATRASO CONTINUA MANDANDO, antes de tudo: quem está vencido há duas semanas vem antes de quem venceu
/// hoje, e a intercalação só desempata dentro de cada data.
/// </summary>
public static class OrdemDaFila
{
    public static IReadOnlyList<Cartao> Intercalar(IEnumerable<Cartao> cartoes, DateOnly hoje) =>
        Intercalar(cartoes, hoje, prioridadePorMateria: null);

    /// <summary>
    /// A mesma ordem, com um desempate a mais: entre cartões que vencem no MESMO dia, os da matéria em
    /// que se está perdendo desempenho vêm primeiro. Ver <c>OndeVocePerde</c>.
    ///
    /// ONDE ESSE DESEMPATE ENTRA IMPORTA MAIS QUE O DESEMPATE. Ele vem DEPOIS da data de vencimento e
    /// ANTES da intercalação:
    ///
    ///   - depois da data, porque atraso não se negocia. Um cartão vencido há duas semanas em Português
    ///     continua vindo antes de um que venceu hoje em Tributário, por pior que esteja Tributário.
    ///     Reordenar por cima do atraso seria deixar o esquecimento acontecer de propósito.
    ///   - antes da intercalação, porque é dentro de um mesmo dia que existe escolha para fazer — e é
    ///     exatamente aí que o registro tem algo a dizer que os cartões não sabem.
    ///
    /// A intercalação por nota continua valendo DENTRO de cada matéria, então a sessão não vira um bloco
    /// de uma nota só. O que muda é qual matéria abre a sessão.
    ///
    /// Matéria fora do mapa recebe <c>OndeVocePerde.Neutra</c>: não é promovida nem punida. Não se
    /// castiga o que não se mediu.
    /// </summary>
    public static IReadOnlyList<Cartao> Intercalar(
        IEnumerable<Cartao> cartoes, DateOnly hoje, IReadOnlyDictionary<Materia, int>? prioridadePorMateria)
    {
        ArgumentNullException.ThrowIfNull(cartoes);

        int Prioridade(Cartao c)
        {
            if (prioridadePorMateria is null || prioridadePorMateria.Count == 0) return 0;
            return prioridadePorMateria.TryGetValue(Materia.De(c.Nota), out var p) ? p : Desempenho.OndeVocePerde.Neutra;
        }

        return cartoes
            // A posição do cartão DENTRO da nota dele. É este número que vira a rodada: todos os
            // "primeiros cartões" saem juntos, depois todos os segundos.
            .GroupBy(c => c.Nota.Valor, StringComparer.Ordinal)
            .SelectMany(g => g
                .OrderBy(c => c.Linha)
                .Select((c, rodada) => (Cartao: c, Rodada: rodada)))
            .OrderBy(x => x.Cartao.AgendamentoOu(hoje).Vence)
            .ThenBy(x => Prioridade(x.Cartao))
            .ThenBy(x => x.Rodada)
            // Dentro da mesma rodada, a ordem entre notas é fixa pelo caminho — sem isto, a fila
            // dependeria da ordem em que o disco devolveu os arquivos, que não é garantida.
            .ThenBy(x => x.Cartao.Nota.Valor, StringComparer.Ordinal)
            .ThenBy(x => x.Cartao.Linha)
            .Select(x => x.Cartao)
            .ToList();
    }
}
