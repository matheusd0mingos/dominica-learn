using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Desempenho;

/// <summary>Por que um registro de questões não faz sentido.</summary>
public enum ProblemaDoLote
{
    SemMateria,
    TotalInvalido,
    AcertosMaiorQueOTotal,
    AcertosNegativos,
    TempoNegativo,
}

/// <summary>
/// Um punhado de questões feitas de uma vez, numa matéria.
///
/// LOTE, E NÃO QUESTÃO A QUESTÃO. Registrar cada questão isolada é o desenho que parece mais completo e
/// é o que ninguém preenche: quem acabou de fazer 40 questões quer digitar "40, acertei 27", não abrir
/// quarenta formulários. O ganho de granularidade seria real e o dado simplesmente não existiria — e um
/// dado que não existe não responde pergunta nenhuma.
///
/// A FONTE É TEXTO LIVRE de propósito ("QConcursos", "prova de 2019", "livro do Ricardo Alexandre"). Uma
/// lista fechada obrigaria a manter um cadastro de bancas e sites que envelhece sozinho, para produzir um
/// filtro que ninguém pediu.
///
/// ISTO É REGISTRO, NÃO CONHECIMENTO — a distinção que o docs/NORTE.md define. Não faz sentido em
/// Markdown (ninguém lê "resolvi 40 questões às 14h32" numa nota) e não é reconstruível a partir de nada:
/// uma série temporal perdida está perdida. Por isso mora num banco DURÁVEL, e não no índice descartável.
/// </summary>
public sealed record LoteDeQuestoes
{
    public Materia Materia { get; }
    public DateTimeOffset Em { get; }
    public int Total { get; }
    public int Acertos { get; }

    /// <summary>Tempo gasto no lote inteiro. Zero quando a pessoa não cronometrou — e não cronometrar é o normal.</summary>
    public TimeSpan Tempo { get; }

    public string Fonte { get; }

    private LoteDeQuestoes(Materia materia, DateTimeOffset em, int total, int acertos, TimeSpan tempo, string fonte)
    {
        Materia = materia;
        Em = em;
        Total = total;
        Acertos = acertos;
        Tempo = tempo;
        Fonte = fonte;
    }

    /// <summary>O acerto do lote, de 0 a 100. Só é comparável entre lotes do MESMO tamanho — ver <see cref="ResumoDeDesempenho"/>.</summary>
    public double Percentual => Total == 0 ? 0 : Acertos * 100.0 / Total;

    public static bool TentarCriar(
        Materia? materia, DateTimeOffset em, int total, int acertos, TimeSpan tempo, string? fonte,
        out LoteDeQuestoes? lote, out ProblemaDoLote? problema)
    {
        lote = null;
        problema = null;

        if (materia is null || !materia.Existe) { problema = ProblemaDoLote.SemMateria; return false; }
        if (total <= 0) { problema = ProblemaDoLote.TotalInvalido; return false; }
        if (acertos < 0) { problema = ProblemaDoLote.AcertosNegativos; return false; }

        // Acertar mais do que se fez é erro de digitação, e gravá-lo produziria um acerto acima de 100%
        // que contamina toda média daquela matéria — sem dar erro em lugar nenhum.
        if (acertos > total) { problema = ProblemaDoLote.AcertosMaiorQueOTotal; return false; }

        if (tempo < TimeSpan.Zero) { problema = ProblemaDoLote.TempoNegativo; return false; }

        lote = new LoteDeQuestoes(materia, em, total, acertos, tempo, (fonte ?? string.Empty).Trim());
        return true;
    }
}

/// <summary>
/// Um período de estudo registrado.
///
/// EXISTE SEPARADO DO LOTE DE QUESTÕES porque as duas coisas respondem perguntas diferentes: hora
/// responde "meu plano é real?", acerto responde "onde estou perdendo". Juntá-las obrigaria a inventar
/// um número de questões para uma leitura, ou um tempo para um lote não cronometrado.
/// </summary>
public sealed record SessaoDeEstudo
{
    public Materia Materia { get; }
    public DateTimeOffset Inicio { get; }
    public TimeSpan Duracao { get; }
    public string Observacao { get; }

    private SessaoDeEstudo(Materia materia, DateTimeOffset inicio, TimeSpan duracao, string observacao)
    {
        Materia = materia;
        Inicio = inicio;
        Duracao = duracao;
        Observacao = observacao;
    }

    /// <summary>
    /// Teto de doze horas numa sessão. Não é desconfiança: é que o erro de digitação mais comum num campo
    /// de minutos é um zero a mais, e "600 minutos" passaria batido enquanto estraga a média da semana.
    /// </summary>
    public static readonly TimeSpan DuracaoMaxima = TimeSpan.FromHours(12);

    public static bool TentarCriar(
        Materia? materia, DateTimeOffset inicio, TimeSpan duracao, string? observacao,
        out SessaoDeEstudo? sessao)
    {
        sessao = null;
        if (materia is null || !materia.Existe) return false;
        if (duracao <= TimeSpan.Zero || duracao > DuracaoMaxima) return false;

        sessao = new SessaoDeEstudo(materia, inicio, duracao, (observacao ?? string.Empty).Trim());
        return true;
    }
}
