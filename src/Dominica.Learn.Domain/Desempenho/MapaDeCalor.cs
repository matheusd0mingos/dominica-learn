using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Desempenho;

/// <summary>
/// Uma resposta de cartão, como o REGISTRO a guarda: quando, em qual matéria, e como foi a lembrança.
///
/// ISTO É REGISTRO, NÃO CONHECIMENTO — o mesmo raciocínio do <see cref="LoteDeQuestoes"/>. O .md do
/// cartão guarda UMA data (a da última resposta): responder cinco vezes deixa uma. A série completa —
/// que é o que responde "estudei todo dia?" e "quanto eu lembro de verdade?" — só existe se cada
/// resposta for anotada na hora, e é isto aqui.
/// </summary>
public sealed record RevisaoDeCartao(DateTimeOffset Em, Materia Materia, Resposta Resposta);

/// <summary>Um dia do mapa de calor: quantas revisões foram feitas nele.</summary>
public sealed record DiaDeCalor(DateOnly Dia, int Revisoes);

/// <summary>
/// O MAPA DE CALOR das revisões — a promessa antiga do painel, agora com o dado que faltava.
///
/// O painel dizia num comentário: "o calor de ontem seria razoável e o do mês passado seria ficção
/// decrescente — vem quando houver registro de sessões". O registro existe e cada resposta é anotada
/// nele; este mapa é a cobrança da promessa.
///
/// SEMPRE O MESMO NÚMERO DE DIAS, terminando hoje: dia sem revisão é ZERO na lista, não buraco — a
/// grade da tela precisa saber qual quadrado é qual sem adivinhar. Mesma regra da previsão de 7 dias.
/// </summary>
public static class MapaDeCalor
{
    public const int SemanasPadrao = 8;

    public static IReadOnlyList<DiaDeCalor> Montar(
        IEnumerable<RevisaoDeCartao> revisoes, DateOnly hoje, int semanas = SemanasPadrao,
        TimeZoneInfo? fuso = null)
    {
        ArgumentNullException.ThrowIfNull(revisoes);
        fuso ??= TimeZoneInfo.Local;
        var dias = Math.Max(1, semanas) * 7;
        var inicio = hoje.AddDays(1 - dias);

        // O DIA É O DO FUSO DE QUEM ESTUDOU (vem por parâmetro — o servidor quase sempre roda em UTC):
        // a revisão de 23h de quinta pertence à quinta — carimbar no fuso do servidor empurraria o fim
        // da noite para o dia seguinte e o "estudei todo dia" mentiria.
        var porDia = revisoes
            .Select(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.Em, fuso).DateTime))
            .Where(d => d >= inicio && d <= hoje)
            .GroupBy(d => d)
            .ToDictionary(g => g.Key, g => g.Count());

        return [.. Enumerable.Range(0, dias)
            .Select(i => inicio.AddDays(i))
            .Select(dia => new DiaDeCalor(dia, porDia.GetValueOrDefault(dia)))];
    }

    /// <summary>
    /// Quanto foi LEMBRADO no conjunto: tudo que não foi "Errei". É a retenção de verdade, medida no
    /// ato — a facilidade média é um eco dela, este número é ela.
    /// </summary>
    public static int PorCentoLembrado(IReadOnlyCollection<RevisaoDeCartao> revisoes)
    {
        ArgumentNullException.ThrowIfNull(revisoes);
        if (revisoes.Count == 0) return 0;
        return (int)Math.Round(100.0 * revisoes.Count(r => r.Resposta != Resposta.Errei) / revisoes.Count);
    }
}
