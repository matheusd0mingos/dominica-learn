using System.Globalization;

namespace Dominica.Learn.Domain.Cartoes;

/// <summary>Como foi a lembrança. É a única entrada que o agendador aceita de quem estuda.</summary>
public enum Resposta
{
    /// <summary>Não lembrei. O cartão volta hoje mesmo.</summary>
    Errei = 0,
    /// <summary>Lembrei com dificuldade.</summary>
    Dificil = 1,
    /// <summary>Lembrei.</summary>
    Bom = 2,
    /// <summary>Lembrei na hora, sem esforço.</summary>
    Facil = 3,
}

/// <summary>
/// Quando o cartão volta, e o quanto ele está fácil.
///
/// ESTE DADO MORA NO ARQUIVO .md, nunca só no banco. É a aplicação mais extrema da regra que rege o
/// projeto: um "reindexar do zero" que apagasse o índice apagaria junto dois anos de repetição espaçada —
/// e o histórico É o valor da repetição espaçada. Sem ele, os cartões voltam todos para o dia zero e o
/// estudo recomeça do nada.
///
/// <see cref="Facilidade"/> é o fator do SM-2 em PORCENTAGEM, como inteiro: 250 = 2,5×. É a escala que o
/// plugin do Obsidian grava no arquivo, então tem de ser esta. Inteiro porque isto vira texto num
/// comentário do arquivo, e ponto flutuante em texto é a receita de "2,5" num computador em português e
/// "2.5" no outro — o mesmo vault, dois formatos.
/// </summary>
public sealed record Agendamento(DateOnly Vence, int IntervaloEmDias, int Facilidade)
{
    /// <summary>Facilidade inicial do SM-2 (2,5). Também o teto prático de um cartão fácil.</summary>
    public const int FacilidadePadrao = 250;

    /// <summary>
    /// Piso da facilidade (1,3). Abaixo disto o intervalo praticamente não cresce e o cartão volta todo
    /// dia para sempre — que é o comportamento correto para algo que você simplesmente não está
    /// aprendendo, mas que precisa de um fundo para não virar divisão por quase-zero.
    /// </summary>
    public const int FacilidadeMinima = 130;

    /// <summary>Um cartão que nunca foi revisado. Vence hoje: é o que o põe na fila do primeiro dia.</summary>
    public static Agendamento Novo(DateOnly hoje) => new(hoje, 0, FacilidadePadrao);

    public bool Vencido(DateOnly hoje) => Vence <= hoje;
}

/// <summary>
/// O agendador SM-2 — o algoritmo do SuperMemo 2, que o Anki e o plugin de repetição espaçada do
/// Obsidian usam em variantes.
///
/// É UMA FUNÇÃO PURA, e isso não é elegância: é o que permite provar o comportamento de dois anos de
/// estudo em milissegundos, sem disco, sem banco e sem relógio. A mesma natureza do
/// <c>Reconciliador</c> e do <c>LayoutDeForca</c>.
///
/// A ESCOLHA DO SM-2 sobre algo mais moderno (FSRS) é deliberada: o SM-2 é o que o ecossistema Obsidian
/// escreve nos arquivos, e o compromisso deste produto é que o vault continue funcionando lá. Um
/// algoritmo melhor com um formato incompatível trocaria a promessa central por alguns pontos de
/// eficiência de revisão.
/// </summary>
public static class AgendadorSM2
{
    public static Agendamento Proximo(Agendamento atual, Resposta resposta, DateOnly hoje)
    {
        // ERREI zera o intervalo e o cartão volta HOJE — não amanhã. Quem errou precisa reencontrar o
        // cartão na mesma sessão; empurrar para o dia seguinte é deixar o erro descansar.
        if (resposta == Resposta.Errei)
            return atual with { Vence = hoje, IntervaloEmDias = 0, Facilidade = AjustarFacilidade(atual.Facilidade, resposta) };

        var facilidade = AjustarFacilidade(atual.Facilidade, resposta);

        var intervalo = atual.IntervaloEmDias switch
        {
            // Os dois primeiros passos são FIXOS, e não calculados: no começo não há histórico que
            // justifique multiplicação nenhuma, e é o que o SM-2 original faz.
            0 => 1,
            1 => resposta == Resposta.Facil ? 4 : 3,
            _ => (int)Math.Round(atual.IntervaloEmDias * (facilidade / 100.0), MidpointRounding.AwayFromZero),
        };

        // "Difícil" nunca deve ALONGAR o intervalo: se foi difícil, o cartão precisa voltar antes do que
        // voltaria, e não depois. Sem este piso, um cartão com facilidade alta ainda cresceria ao ser
        // marcado como difícil — e a pessoa veria o sistema se afastando justo do que ela não sabe.
        if (resposta == Resposta.Dificil)
            intervalo = Math.Min(intervalo, Math.Max(1, atual.IntervaloEmDias));

        intervalo = Math.Max(1, intervalo);

        // Teto de dez anos: sem ele, um cartão fácil revisado por muito tempo estoura o DateOnly e o
        // agendamento vira exceção no meio de uma sessão de estudo.
        intervalo = Math.Min(intervalo, 3650);

        return new Agendamento(hoje.AddDays(intervalo), intervalo, facilidade);
    }

    /// <summary>
    /// A curva do SM-2, na mesma escala de porcentagem da facilidade (250 = 2,5×). Errar custa caro e
    /// acertar com facilidade rende pouco — de propósito,
    /// porque a facilidade sobe devagar e desce rápido é o que faz o algoritmo reagir a um assunto que
    /// azedou sem virar montanha-russa com um acerto de sorte.
    /// </summary>
    private static int AjustarFacilidade(int facilidade, Resposta resposta)
    {
        var novo = facilidade + resposta switch
        {
            Resposta.Errei => -20,
            Resposta.Dificil => -15,
            Resposta.Bom => 0,
            Resposta.Facil => +15,
            _ => 0,
        };
        return Math.Clamp(novo, Agendamento.FacilidadeMinima, 400);
    }
}

/// <summary>
/// O agendamento como ele aparece DENTRO do arquivo: <c>&lt;!--SR:!2026-08-14,6,250--&gt;</c>
///
/// O formato é o do plugin obsidian-spaced-repetition, e copiá-lo é o ponto: o mesmo arquivo continua
/// sendo revisável no Obsidian, com o histórico que você construiu aqui. Inventar um formato próprio
/// seria mais bonito e quebraria a promessa central do produto.
///
/// Comentário HTML porque o Markdown o esconde na leitura: o agendamento fica no arquivo, versionável e
/// sincronizável, sem poluir o texto de quem está estudando.
/// </summary>
public static class MarcaDeAgendamento
{
    public const string Prefixo = "<!--SR:";
    private const string Sufixo = "-->";

    public static string Escrever(Agendamento a) =>
        $"{Prefixo}!{a.Vence.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)},{a.IntervaloEmDias},{a.Facilidade}{Sufixo}";

    /// <summary>
    /// Lê a marca. Devolve false para qualquer coisa fora do formato — inclusive marca truncada por uma
    /// sincronização interrompida. Cartão sem agendamento legível é tratado como novo, que é o pior caso
    /// aceitável: revisar de novo custa um minuto, e adivinhar uma data custaria a confiança no sistema.
    /// </summary>
    public static bool TentarLer(string texto, out Agendamento? agendamento)
    {
        agendamento = null;

        var inicio = texto.IndexOf(Prefixo, StringComparison.Ordinal);
        if (inicio < 0) return false;
        var fim = texto.IndexOf(Sufixo, inicio, StringComparison.Ordinal);
        if (fim < 0) return false;

        var miolo = texto[(inicio + Prefixo.Length)..fim].Trim().TrimStart('!');
        var partes = miolo.Split(',');
        if (partes.Length != 3) return false;

        if (!DateOnly.TryParseExact(partes[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var vence)) return false;
        if (!int.TryParse(partes[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var intervalo)) return false;
        if (!int.TryParse(partes[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var facilidade)) return false;

        agendamento = new Agendamento(vence, Math.Max(0, intervalo),
            Math.Clamp(facilidade, Agendamento.FacilidadeMinima, 400));
        return true;
    }

    /// <summary>O texto sem a marca, para exibir o cartão sem o comentário no meio.</summary>
    public static string Remover(string texto)
    {
        var inicio = texto.IndexOf(Prefixo, StringComparison.Ordinal);
        if (inicio < 0) return texto;
        var fim = texto.IndexOf(Sufixo, inicio, StringComparison.Ordinal);
        return fim < 0 ? texto : (texto[..inicio] + texto[(fim + Sufixo.Length)..]).TrimEnd();
    }
}
