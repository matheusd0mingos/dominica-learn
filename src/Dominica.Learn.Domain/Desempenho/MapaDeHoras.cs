using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Desempenho;

/// <summary>Um dia do histórico: quanto tempo foi estudado nele (zero num dia sem estudo, não buraco).</summary>
public sealed record DiaDeHoras(DateOnly Dia, TimeSpan Estudado);

/// <summary>
/// O histórico de horas resumido: a grade dia-a-dia mais os três números que a pessoa quer ler de relance.
///
/// São TRÊS leituras diferentes de propósito, porque respondem perguntas diferentes:
///   • NestaSemana  — "quanto já fiz esta semana?" (de segunda até hoje)
///   • MediaPorDia   — "quando eu sento, quanto rende?" (média SÓ nos dias que teve estudo — um domingo
///                     de descanso não devia derrubar a média e fazer parecer que você estuda pouco)
///   • Sequencia     — "há quantos dias seguidos eu não falho?" (o número que vicia no GitHub)
/// </summary>
public sealed record HistoricoDeHoras(
    IReadOnlyList<DiaDeHoras> Dias,
    TimeSpan Total,
    TimeSpan NestaSemana,
    TimeSpan MediaPorDia,
    int DiasEstudados,
    int Sequencia)
{
    /// <summary>Sem nenhuma hora registrada na janela — a tela esconde o mapa em vez de mostrar grade zerada.</summary>
    public bool Vazio => Total == TimeSpan.Zero;
}

/// <summary>
/// O HISTÓRICO DE HORAS de estudo — o mesmo mapa de calor do GitHub, mas medindo TEMPO, não revisões.
///
/// O painel já tinha o calor das revisões (quantos cartões por dia) e as horas apareciam só como um total
/// solto. Faltava o que dá a sensação de "máquina de estudo": ver a semana, a média e a sequência, e o
/// histórico em quadradinhos que cobra a falha de um dia em branco. É a leitura irmã do MapaDeCalor, e de
/// propósito com a MESMA grade — mesmos dias, terminando hoje — para as duas telas se lerem juntas.
///
/// A CLASSE É PURA: recebe as sessões e a data de hoje, devolve os números. Quem lê o registro é a aplicação.
/// </summary>
public static class MapaDeHoras
{
    /// <summary>Oito semanas — o mesmo horizonte do mapa de calor das revisões, para as grades baterem.</summary>
    public const int SemanasPadrao = 8;

    public static HistoricoDeHoras Montar(
        IEnumerable<SessaoDeEstudo> sessoes, DateOnly hoje, int semanas = SemanasPadrao,
        TimeZoneInfo? fuso = null)
    {
        ArgumentNullException.ThrowIfNull(sessoes);
        fuso ??= TimeZoneInfo.Local;
        var dias = Math.Max(1, semanas) * 7;
        var inicio = hoje.AddDays(1 - dias);

        // O DIA É O DO FUSO DE QUEM ESTUDOU (por parâmetro — o servidor quase sempre roda em UTC): a
        // sessão que começou 23h de quinta pertence à quinta. Carimbar no fuso do servidor empurraria o
        // fim da noite para o dia seguinte e o "estudei todo dia" mentiria. Mesma regra do MapaDeCalor —
        // se as duas grades usassem fusos diferentes, um dia acenderia numa e não na outra.
        var porDia = new Dictionary<DateOnly, TimeSpan>();
        foreach (var s in sessoes)
        {
            var dia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.Inicio, fuso).DateTime);
            if (dia < inicio || dia > hoje) continue;
            porDia[dia] = porDia.GetValueOrDefault(dia) + s.Duracao;
        }

        var grade = Enumerable.Range(0, dias)
            .Select(i => inicio.AddDays(i))
            .Select(dia => new DiaDeHoras(dia, porDia.GetValueOrDefault(dia)))
            .ToList();

        var total = SomarNoPeriodo(porDia.Values);
        var diasEstudados = porDia.Values.Count(t => t > TimeSpan.Zero);
        var media = diasEstudados == 0 ? TimeSpan.Zero : total / diasEstudados;

        return new HistoricoDeHoras(
            grade, total, NaSemanaDe(porDia, hoje), media, diasEstudados, SequenciaAte(porDia, hoje));
    }

    /// <summary>
    /// Horas desde a SEGUNDA desta semana até hoje. "Semana" para um concurseiro começa na segunda: contar
    /// os últimos 7 dias corridos misturaria o fim de semana passado com o começo desta e o número não
    /// casaria com o quadro branco na parede.
    /// </summary>
    private static TimeSpan NaSemanaDe(IReadOnlyDictionary<DateOnly, TimeSpan> porDia, DateOnly hoje)
    {
        // DayOfWeek: domingo = 0, segunda = 1 … sábado = 6. Quero quantos dias recuar até a segunda.
        var recuo = ((int)hoje.DayOfWeek + 6) % 7;   // segunda → 0, domingo → 6
        var segunda = hoje.AddDays(-recuo);
        var soma = TimeSpan.Zero;
        for (var d = segunda; d <= hoje; d = d.AddDays(1))
            soma += porDia.GetValueOrDefault(d);
        return soma;
    }

    /// <summary>
    /// A sequência atual: quantos dias seguidos, terminando em hoje, tiveram estudo. Um dia de hoje ainda
    /// em branco NÃO zera a sequência — ela conta a partir de ontem — porque às 8h da manhã você ainda não
    /// estudou e não é justo dizer que a sequência acabou antes do dia começar. Some hoje quebrado, some
    /// ontem em branco: a sequência morre.
    /// </summary>
    private static int SequenciaAte(IReadOnlyDictionary<DateOnly, TimeSpan> porDia, DateOnly hoje)
    {
        bool Estudou(DateOnly d) => porDia.GetValueOrDefault(d) > TimeSpan.Zero;

        // Onde a contagem começa: hoje se já estudou hoje; senão ontem (o dia ainda não fechou).
        var cursor = Estudou(hoje) ? hoje : hoje.AddDays(-1);
        var n = 0;
        while (Estudou(cursor))
        {
            n++;
            cursor = cursor.AddDays(-1);
        }
        return n;
    }

    // TimeSpan não tem Sum() — dobrar o Add à mão em vez de somar ticks (long) evita estouro teórico e lê melhor.
    private static TimeSpan SomarNoPeriodo(IEnumerable<TimeSpan> tempos)
    {
        var total = TimeSpan.Zero;
        foreach (var t in tempos) total += t;
        return total;
    }
}
