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

    private static string Explicar(ProblemaDoLote? p) => p switch
    {
        ProblemaDoLote.SemMateria => "Escolha a matéria — é ela que responde onde você está perdendo.",
        ProblemaDoLote.TotalInvalido => "Quantas questões você fez?",
        ProblemaDoLote.AcertosMaiorQueOTotal => "Os acertos não podem passar do total de questões.",
        ProblemaDoLote.AcertosNegativos => "Acertos não podem ser negativos.",
        _ => "O tempo não pode ser negativo.",
    };
}
