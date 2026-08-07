namespace Dominica.Learn.Domain.Cartoes;

/// <summary>
/// O cartão que você erra sempre — e o que fazer com ele, que NÃO é repeti-lo mais.
///
/// O PROBLEMA REAL: meia dúzia de cartões ruins come a sessão inteira, todo dia, para sempre. Errar
/// devolve o cartão à fila de hoje (é o certo), então um cartão que não entra na cabeça reaparece na
/// sessão seguinte, e na outra. O sistema fica ocupado com ele enquanto os outros esperam, a sessão
/// diária cresce, e a conclusão que a pessoa tira é a pior possível: "eu tenho memória ruim".
///
/// QUASE SEMPRE O PROBLEMA É O CARTÃO. Um cartão que falha muitas vezes costuma ter um defeito de
/// formulação, e sempre o mesmo tipo: ele pergunta duas coisas de uma vez ("cite as três exceções e o
/// prazo"), ou a resposta é um parágrafo em vez de um fato, ou a pergunta não tem contexto suficiente
/// para ter uma resposta só. Repetir isso mais vezes não conserta nada — reescrever conserta. É por isso
/// que este arquivo existe: detectar não vale nada sem dizer o que fazer.
///
/// COMO SE DETECTA SEM ESTADO NOVO: pela FACILIDADE, que já mora no arquivo .md. Ela começa em 250 e cai
/// a cada tropeço (−20 ao errar, −15 no "difícil"), com piso em 130. Uma facilidade baixa é o registro
/// acumulado dos tropeços — não é preciso guardar contador nenhum, e a conta continua certa se a pessoa
/// revisar pelo Obsidian, ou depois de um "reindexar do zero".
///
/// O QUE ESTA CLASSE NÃO FAZ: não conta erros. O formato do plugin guarda a facilidade, não o número de
/// falhas, e "−20 por erro" convive com "−15 por difícil" — qualquer contagem derivada daí seria um
/// palpite apresentado como número. O que se diz na tela é o que se sabe: a facilidade caiu até tanto.
/// </summary>
public static class CartaoProblema
{
    /// <summary>
    /// A partir daqui o cartão está custando caro: 170 é a facilidade depois de quatro tropeços.
    ///
    /// Quatro é cedo o bastante para o aviso chegar antes de o cartão ter comido dez sessões, e tarde o
    /// bastante para não acusar quem errou um dia ruim. O Anki marca em oito falhas; aqui o sinal aparece
    /// antes porque ele não faz nada sozinho — só avisa.
    /// </summary>
    public const int FacilidadeDeProblema = 170;

    /// <summary>
    /// O cartão está custando mais do que devia? Cartão inédito nunca é problema — ele ainda não teve
    /// chance de ser um.
    /// </summary>
    public static bool Eh(Cartao cartao)
    {
        ArgumentNullException.ThrowIfNull(cartao);
        return cartao.Agendamento is { } a && a.Facilidade <= FacilidadeDeProblema;
    }

    /// <summary>No piso da facilidade: o cartão volta praticamente todo dia, e vai continuar voltando.</summary>
    public static bool NoPiso(Cartao cartao) =>
        cartao.Agendamento is { } a && a.Facilidade <= Agendamento.FacilidadeMinima;

    /// <summary>
    /// O que dizer sobre ele. Nulo quando não há o que dizer.
    ///
    /// A FRASE TERMINA COM O QUE FAZER, e não com o diagnóstico. "Facilidade 1,30×" é um número; "o
    /// problema costuma ser o cartão, não a sua memória — reescreva-o" é uma saída. Um aviso que só
    /// diagnostica deixa a pessoa exatamente onde ela estava, agora também culpada.
    /// </summary>
    public static string? Diagnostico(Cartao cartao)
    {
        if (!Eh(cartao)) return null;

        var facilidade = cartao.Agendamento!.Facilidade / 100.0;

        return NoPiso(cartao)
            ? $"Este cartão está no piso da facilidade ({facilidade:0.00}×, começa em 2,50×): ele volta " +
              "quase todo dia e vai continuar voltando. Quando um cartão falha tanto, o problema quase " +
              "sempre é ele, não a sua memória — costuma perguntar duas coisas de uma vez, ou pedir um " +
              "parágrafo em vez de um fato. Vale reescrevê-lo em dois cartões menores, ou suspendê-lo."
            : $"Este cartão já caiu para {facilidade:0.00}× de facilidade (começa em 2,50×). Se ele " +
              "continuar voltando, releia a pergunta: em geral ela está pedindo mais de uma resposta.";
    }

    /// <summary>
    /// A frente do cartão em uma linha, para caber num aviso.
    ///
    /// MORA AQUI, e não em cada tela: o painel e a revisão mostram o mesmo cartão, e dois cortes
    /// diferentes fariam a mesma pergunta aparecer com dois títulos — que é o bastante para alguém achar
    /// que são dois cartões.
    /// </summary>
    public static string Resumir(string frente)
    {
        var limpa = (frente ?? string.Empty).Replace('\n', ' ').Trim();
        return limpa.Length <= 48 ? limpa : limpa[..47].TrimEnd() + "…";
    }

    /// <summary>
    /// Os cartões-problema, do pior para o menos pior — para uma lista que serve de fila de reescrita.
    /// </summary>
    public static IReadOnlyList<Cartao> Ordenados(IEnumerable<Cartao> cartoes)
    {
        ArgumentNullException.ThrowIfNull(cartoes);

        return cartoes
            .Where(Eh)
            // Suspenso fica de fora: ele já foi tirado da frente de propósito, e listá-lo aqui seria
            // pedir uma decisão que a pessoa já tomou.
            .Where(c => !c.Suspenso)
            .OrderBy(c => c.Agendamento!.Facilidade)
            .ThenBy(c => c.Nota.Valor, StringComparer.Ordinal)
            .ThenBy(c => c.Linha)
            .ToList();
    }
}
