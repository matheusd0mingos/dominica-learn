using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

/// <summary>
/// Registrar o que foi feito, e devolver o que isso significa.
///
/// SERVIÇO PRÓPRIO, e não mais um método no ServicoDeCartoes: aquele guarda a invariante da revisão
/// espaçada e escreve nos arquivos do vault. Este não toca em arquivo nenhum — grava num banco durável e
/// lê de volta. Juntá-los faria uma classe onde metade dos métodos escreve em .md e a outra metade em
/// Postgres, e a regra "conhecimento no disco, registro no banco" ficaria impossível de enxergar.
/// </summary>
public sealed class ServicoDeDesempenho(
    IRegistroDeEstudo registro,
    IRelogio relogio,
    ILogger<ServicoDeDesempenho> log)
{
    /// <summary>
    /// A janela padrão do painel: quatro semanas.
    ///
    /// Não é arbitrário — é o horizonte em que uma mudança de rotina já apareceu e um mês ruim antigo já
    /// saiu. Uma janela de um ano diria que você vai bem em algo que azedou em março; uma de uma semana
    /// oscilaria com um único simulado difícil.
    /// </summary>
    public static readonly TimeSpan JanelaPadrao = TimeSpan.FromDays(28);

    public async Task<Resultado<int>> RegistrarQuestoesAsync(
        Materia materia, int total, int acertos, TimeSpan tempo, string? fonte, CancellationToken ct = default)
    {
        if (!LoteDeQuestoes.TentarCriar(materia, relogio.Agora, total, acertos, tempo, fonte, out var lote, out var problema)
            || lote is null)
            return Resultado<int>.Falha(MotivoDaFalha.Invalida, Explicar(problema));

        await registro.RegistrarQuestoesAsync(lote, ct);
        log.LogInformation("Questões registradas: {Acertos}/{Total} em {Materia}.", acertos, total, materia.Rotulo);

        return Resultado<int>.Sucesso(total);
    }

    public async Task<Resultado<TimeSpan>> RegistrarSessaoAsync(
        Materia materia, TimeSpan duracao, string? observacao, CancellationToken ct = default)
    {
        if (!SessaoDeEstudo.TentarCriar(materia, relogio.Agora, duracao, observacao, out var sessao) || sessao is null)
            return Resultado<TimeSpan>.Falha(MotivoDaFalha.Invalida,
                $"Informe a matéria e um tempo entre 1 minuto e {SessaoDeEstudo.DuracaoMaxima.TotalHours:0} horas.");

        await registro.RegistrarSessaoAsync(sessao, ct);
        log.LogInformation("Sessão registrada: {Minutos} min em {Materia}.", duracao.TotalMinutes, materia.Rotulo);

        return Resultado<TimeSpan>.Sucesso(duracao);
    }

    /// <summary>O desempenho do período. Sem registro nenhum devolve o resumo vazio, não um erro.</summary>
    public async Task<ResumoDeDesempenho> ResumoAsync(TimeSpan? janela = null, CancellationToken ct = default)
    {
        var desde = relogio.Agora - (janela ?? JanelaPadrao);
        return CalculoDeDesempenho.Montar(
            await registro.QuestoesAsync(desde, ct),
            await registro.SessoesAsync(desde, ct));
    }

    /// <summary>
    /// O mapa de calor das revisões e a retenção real do período — o que o registro de respostas compra.
    /// Ver <see cref="MapaDeCalor"/>.
    /// </summary>
    public async Task<(IReadOnlyList<DiaDeCalor> Dias, int PorCentoLembrado, int Total)> CalorAsync(
        int semanas = MapaDeCalor.SemanasPadrao, CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(relogio.Agora.DateTime);
        var desde = relogio.Agora - TimeSpan.FromDays(semanas * 7);
        var revisoes = await registro.RevisoesAsync(desde, ct);
        return (MapaDeCalor.Montar(revisoes, hoje, semanas, relogio.Fuso), MapaDeCalor.PorCentoLembrado(revisoes), revisoes.Count);
    }

    /// <summary>
    /// O histórico de HORAS: a grade dia-a-dia mais semana, média e sequência. É a leitura irmã do calor —
    /// aquele conta revisões, este mede tempo — e usa a mesma janela para as duas grades se lerem juntas.
    /// Ver <see cref="MapaDeHoras"/>.
    /// </summary>
    public async Task<HistoricoDeHoras> HorasAsync(
        int semanas = MapaDeHoras.SemanasPadrao, CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(relogio.Agora.DateTime);
        var desde = relogio.Agora - TimeSpan.FromDays(semanas * 7);
        return MapaDeHoras.Montar(await registro.SessoesAsync(desde, ct), hoje, semanas, relogio.Fuso);
    }

    /// <summary>
    /// Quantas semanas cobrem o histórico INTEIRO — de quando se começou a estudar até hoje.
    ///
    /// É o que a tela precisa para oferecer "desde o início" sem chutar um número. Sem isto, o painel
    /// só sabia perguntar por uma janela fixa, e quem estuda há um ano via as últimas oito semanas
    /// como se fossem tudo — a régua do próprio esforço parava dois meses atrás.
    ///
    /// Conta a partir do registro mais antigo de QUALQUER natureza (sessão de tempo ou revisão de
    /// cartão): quem só usou o cronômetro e quem só revisou cartões têm as duas um começo, e usar só
    /// as sessões cortaria o histórico de quem revisa sem cronometrar.
    ///
    /// Devolve no mínimo a janela padrão — um histórico de três dias não deve encolher a grade a
    /// ponto de não haver o que olhar.
    /// </summary>
    public async Task<int> SemanasDesdeOInicioAsync(CancellationToken ct = default)
    {
        // MinValue como piso: o filtro é `>= desde` sobre timestamptz, cujo domínio começa muito antes
        // de qualquer data que este produto possa ter gravado. É "tudo" sem inventar uma data de corte.
        var sessoes = await registro.SessoesAsync(DateTimeOffset.MinValue, ct);
        var revisoes = await registro.RevisoesAsync(DateTimeOffset.MinValue, ct);

        DateTimeOffset? primeiro = null;
        foreach (var s in sessoes) if (primeiro is null || s.Inicio < primeiro) primeiro = s.Inicio;
        foreach (var r in revisoes) if (primeiro is null || r.Em < primeiro) primeiro = r.Em;
        if (primeiro is null) return MapaDeHoras.SemanasPadrao;

        var dias = (relogio.Agora - primeiro.Value).TotalDays;
        // +1 semana de folga: a grade termina HOJE e começa no início da semana mais antiga; sem a
        // folga, o primeiro dia registrado poderia cair fora por algumas horas.
        var semanas = (int)Math.Ceiling(dias / 7d) + 1;
        return Math.Max(MapaDeHoras.SemanasPadrao, semanas);
    }

    /// <summary>
    /// Horas estudadas POR MATÉRIA nesta semana (da segunda até agora). É o "feito" que o plano da semana
    /// mede contra a meta de cada matéria — e sai do MESMO registro de sessões, siga a pessoa o plano ou não.
    /// </summary>
    public async Task<IReadOnlyDictionary<Materia, TimeSpan>> HorasPorMateriaNaSemanaAsync(CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(relogio.Agora.DateTime);
        var recuo = ((int)hoje.DayOfWeek + 6) % 7;   // segunda → 0, domingo → 6
        var segunda = hoje.AddDays(-recuo);
        var desde = new DateTimeOffset(segunda.ToDateTime(TimeOnly.MinValue), relogio.Agora.Offset);

        var sessoes = await registro.SessoesAsync(desde, ct);
        return sessoes
            .GroupBy(s => s.Materia)
            .ToDictionary(g => g.Key, g => new TimeSpan(g.Sum(s => s.Duracao.Ticks)));
    }

    /// <summary>Quantos lançamentos a tela mostra. Uma tela, não um histórico — ver <see cref="UltimosAsync"/>.</summary>
    public const int UltimosPadrao = 12;

    /// <summary>
    /// O que foi registrado por último, para conferir e apagar.
    ///
    /// ISTO É O QUE FAZ O REGISTRO SER USADO. Um número que entra e não sai é um número em que ninguém
    /// confia: quem digita "300" no lugar de "30" e descobre que não há conserto não corrige o erro —
    /// para de registrar. A lista curta ao lado do botão de registrar é o que devolve a reversibilidade
    /// ao gesto, e é por isso que ela fica junto do gesto, e não numa tela de histórico que ninguém abre.
    /// </summary>
    public Task<IReadOnlyList<LancamentoRegistrado>> UltimosAsync(
        int limite = UltimosPadrao, CancellationToken ct = default) => registro.UltimosAsync(limite, ct);

    /// <summary>
    /// CORRIGE um lançamento no lugar — o "300" que era para ser "30", a matéria errada, o tempo que
    /// ficou faltando.
    ///
    /// A VALIDAÇÃO É A MESMA DE REGISTRAR, e passa pelo mesmo domínio: editar não pode ser a porta dos
    /// fundos por onde entra um lote com mais acertos que questões ou uma sessão de dezoito horas. Por
    /// isso o valor novo é montado como se fosse um lançamento novo (<c>TentarCriar</c>) antes de virar
    /// UPDATE; o que o domínio recusaria ao registrar, ele recusa ao corrigir, com a mesma frase.
    ///
    /// A DATA NÃO ENTRA. Corrigir a contagem de ontem não move o estudo para hoje — ver
    /// <see cref="IRegistroDeEstudo.AtualizarAsync"/>.
    /// </summary>
    public async Task<Resultado<int>> EditarLancamentoAsync(
        TipoDeLancamento tipo, int id, Materia materia, int total, int acertos, TimeSpan tempo,
        string? fonte, string? observacao, CancellationToken ct = default)
    {
        if (tipo == TipoDeLancamento.Questoes)
        {
            if (!LoteDeQuestoes.TentarCriar(materia, relogio.Agora, total, acertos, tempo, fonte, out _, out var problema))
                return Resultado<int>.Falha(MotivoDaFalha.Invalida, Explicar(problema));
        }
        else if (!SessaoDeEstudo.TentarCriar(materia, relogio.Agora, tempo, observacao, out _))
        {
            return Resultado<int>.Falha(MotivoDaFalha.Invalida,
                $"Informe a matéria e um tempo entre 1 minuto e {SessaoDeEstudo.DuracaoMaxima.TotalHours:0} horas.");
        }

        if (!await registro.AtualizarAsync(new LancamentoEditado(tipo, id, materia, total, acertos, tempo, fonte, observacao), ct))
            // NÃO ENCONTRADO, e não "erro": o lançamento pode ter sido apagado noutra aba enquanto esta
            // estava aberta. Mesma leitura do apagar.
            return Resultado<int>.NaoEncontrada("O lançamento");

        log.LogInformation("Lançamento de {Tipo} corrigido ({Id}).", tipo, id);
        return Resultado<int>.Sucesso(1);
    }

    /// <summary>
    /// Apaga um lançamento. Para corrigir sem apagar, ver <see cref="EditarLancamentoAsync"/>.
    /// </summary>
    public async Task<Resultado<int>> ApagarLancamentoAsync(
        TipoDeLancamento tipo, int id, CancellationToken ct = default)
    {
        if (!await registro.ApagarAsync(tipo, id, ct))
            // NÃO ENCONTRADO, e não "erro": o lançamento pode ter sido apagado noutra aba. Dizer que
            // falhou faria a pessoa tentar de novo o que já está feito.
            return Resultado<int>.NaoEncontrada("O lançamento");

        log.LogInformation("Lançamento de {Tipo} apagado ({Id}).", tipo, id);
        return Resultado<int>.Sucesso(1);
    }

    private static string Explicar(ProblemaDoLote? p) => p switch
    {
        ProblemaDoLote.SemMateria => "Escolha a matéria — é ela que responde onde você está perdendo.",
        ProblemaDoLote.TotalInvalido => "Quantas questões você fez?",
        ProblemaDoLote.AcertosMaiorQueOTotal => "Os acertos não podem passar do total de questões.",
        ProblemaDoLote.AcertosNegativos => "Acertos não podem ser negativos.",
        _ => "O tempo não pode ser negativo.",
    };
}
