using System.Globalization;
using System.Text;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Estudo;

/// <summary>Uma meta da semana: um tanto de horas para uma matéria. Meta, não obrigação.</summary>
public sealed record MetaDaSemana(Materia Materia, TimeSpan Meta);

/// <summary>
/// Uma matéria candidata ao plano, com o sinal de fraqueza que decide seu peso.
///
/// <see cref="PorCentoAcerto"/> é NULO quando não há amostra que sustente um número — nunca estudou
/// questões, ou fez tão poucas que o percentual é ruído. Nulo não é "vai bem": é "não sei", e o plano
/// trata "não sei" como fraqueza média, porque uma matéria que você nunca testou é um risco, não um
/// conforto. Ver <see cref="DesempenhoDaMateria.TemAmostraSuficiente"/>.
/// </summary>
public sealed record CandidataAoPlano(Materia Materia, double? PorCentoAcerto);

/// <summary>
/// O PLANO DA SEMANA — metas de horas por matéria, propostas pela sua fraqueza, mudáveis, e que você não
/// é obrigado a seguir.
///
/// A regra do produto é que o plano é uma SUGESTÃO, não uma jaula. Por isso três coisas:
///   • o registro de horas é independente do plano — estudar fora dele conta igual (ver ServicoDeDesempenho);
///   • o plano é PROPOSTO a partir do que você erra, mas você edita, apaga, ou ignora;
///   • ele mora numa nota do vault, então abre no Obsidian e sincroniza — o plano é seu.
///
/// O formato é uma lista comum, legível no Obsidian:
///
///     # Plano da semana
///     - Tributário: 5h
///     - Direito: 3h
///     - Português: 1h30
///
/// COMO A FRAQUEZA VIRA HORAS: cada matéria ganha um peso = quanto você ERRA (100 − acerto), com um piso
/// para a matéria forte não sumir do plano — manter o que já sabe também é estudo. As horas do orçamento
/// se repartem na proporção dos pesos e são arredondadas para meia hora, o passo em que um plano de estudo
/// se pensa de verdade.
///
/// A CLASSE É PURA: texto e números entram, texto e números saem. Quem lê a nota e o desempenho é a aplicação.
/// </summary>
public static class PlanoDaSemana
{
    /// <summary>Onde o plano mora — uma nota na raiz do vault, irmã do "Ciclo de estudos".</summary>
    public const string CaminhoDaNota = "Plano da semana.md";

    /// <summary>Orçamento semanal padrão quando não há histórico para estimar: doze horas, ~2h/dia.</summary>
    public static readonly TimeSpan OrcamentoPadrao = TimeSpan.FromHours(12);

    /// <summary>Meia hora — o passo em que metas de estudo se pensam; "37 min de Direito" não é plano.</summary>
    public static readonly TimeSpan Passo = TimeSpan.FromMinutes(30);

    /// <summary>Piso por matéria no plano proposto: entrar no plano é entrar com pelo menos meia hora.</summary>
    public static readonly TimeSpan MinimoPorMateria = TimeSpan.FromMinutes(30);

    // Peso mínimo da matéria forte (acerto alto) — mantém no plano quem você já sabe, com pouco tempo, em
    // vez de zerar. Peso da matéria sem amostra: fraqueza média, porque "não testei" é risco, não conforto.
    private const double PesoMinimo = 20.0;
    private const double PesoSemAmostra = 50.0;

    /// <summary>Lê as metas da nota. Linhas que não são "- Matéria: tempo" são ignoradas.</summary>
    public static IReadOnlyList<MetaDaSemana> Ler(string? conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo)) return [];

        var metas = new List<MetaDaSemana>();
        foreach (var linha in conteudo.Replace("\r\n", "\n").Split('\n'))
        {
            var s = linha.TrimStart();
            if (s.Length < 3 || s[0] is not ('-' or '*' or '+') || s[1] != ' ') continue;

            var corpo = s[2..];
            var doisPontos = corpo.IndexOf(':');
            if (doisPontos < 0) continue;

            var nome = corpo[..doisPontos].Trim();
            var materia = Materia.De(nome);
            if (!materia.Existe) continue;

            var meta = LerDuracao(corpo[(doisPontos + 1)..]);
            if (meta > TimeSpan.Zero) metas.Add(new MetaDaSemana(materia, meta));
        }
        return metas;
    }

    /// <summary>A nota do plano — o título fixo mais a lista de metas.</summary>
    public static string Escrever(IReadOnlyList<MetaDaSemana> metas)
    {
        ArgumentNullException.ThrowIfNull(metas);
        var sb = new StringBuilder();
        sb.Append("# Plano da semana\n\n");
        if (metas.Count == 0)
            sb.Append("Sem metas ainda. Peça uma sugestão pela sua fraqueza, ou escreva as suas.\n");
        foreach (var m in metas)
            sb.Append("- ").Append(m.Materia.Nome).Append(": ").Append(FormatarDuracao(m.Meta)).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Propõe as metas: reparte o orçamento entre as matérias na proporção do quanto você erra cada uma.
    /// Matéria mais fraca ganha mais horas; a forte não some, ganha o mínimo. Arredonda para meia hora.
    /// </summary>
    public static IReadOnlyList<MetaDaSemana> Propor(IEnumerable<CandidataAoPlano> candidatas, TimeSpan orcamento)
    {
        ArgumentNullException.ThrowIfNull(candidatas);
        var lista = candidatas.Where(c => c.Materia.Existe).ToList();
        if (lista.Count == 0 || orcamento <= TimeSpan.Zero) return [];

        var pesos = lista.Select(PesoDe).ToList();
        var somaDosPesos = pesos.Sum();
        if (somaDosPesos <= 0) return [];

        var passoMin = Passo.TotalMinutes;
        var minMin = MinimoPorMateria.TotalMinutes;

        var metas = new List<MetaDaSemana>();
        for (var i = 0; i < lista.Count; i++)
        {
            var bruto = orcamento.TotalMinutes * pesos[i] / somaDosPesos;
            var arredondado = Math.Round(bruto / passoMin, MidpointRounding.AwayFromZero) * passoMin;
            var minutos = Math.Max(arredondado, minMin);
            metas.Add(new MetaDaSemana(lista[i].Materia, TimeSpan.FromMinutes(minutos)));
        }

        // Mais fraca (mais horas) no topo — o plano se lê de cima para baixo por prioridade.
        return [.. metas.OrderByDescending(m => m.Meta)
                        .ThenBy(m => m.Materia.Rotulo, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static double PesoDe(CandidataAoPlano c) =>
        c.PorCentoAcerto is double acerto
            ? Math.Max(PesoMinimo, 100.0 - acerto)   // erra mais → pesa mais; forte não zera (piso)
            : PesoSemAmostra;                          // não testou → fraqueza média

    /// <summary>"5h" / "1h30" / "45min" — a mesma leitura curta do painel de horas.</summary>
    public static string FormatarDuracao(TimeSpan t)
    {
        var min = (int)Math.Round(t.TotalMinutes);
        if (min <= 0) return "0min";
        var h = min / 60;
        var m = min % 60;
        if (h == 0) return $"{m}min";
        return m == 0 ? $"{h}h" : $"{h}h{m:00}";
    }

    /// <summary>
    /// Lê "5h", "1h30", "1h30min", "90min", "45 min", "2" (horas). Tolerante de propósito: a pessoa
    /// escreve o plano na mão no Obsidian, e recusar "1h 30" por causa de um espaço seria hostil.
    /// </summary>
    public static TimeSpan LerDuracao(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return TimeSpan.Zero;
        var t = texto.Trim().ToLowerInvariant();

        // "...min" sem "h": são minutos puros.
        if (t.Contains("min") && !t.Contains('h'))
            return TimeSpan.FromMinutes(PrimeiroInteiro(t));

        var iH = t.IndexOf('h');
        if (iH >= 0)
        {
            var horas = PrimeiroInteiro(t[..iH]);
            var resto = t[(iH + 1)..];
            var minutos = string.IsNullOrWhiteSpace(resto.Replace("min", "").Trim()) ? 0 : PrimeiroInteiro(resto);
            return TimeSpan.FromHours(horas) + TimeSpan.FromMinutes(minutos);
        }

        // Só um número, sem unidade: horas — é como se escreve "Direito: 2".
        return TimeSpan.FromHours(PrimeiroInteiro(t));
    }

    private static int PrimeiroInteiro(string texto)
    {
        var digitos = new string([.. texto.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit)]);
        return int.TryParse(digitos, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}
