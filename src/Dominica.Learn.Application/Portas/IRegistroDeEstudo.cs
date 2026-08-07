using Dominica.Learn.Domain.Desempenho;

namespace Dominica.Learn.Application.Portas;

/// <summary>
/// O que a pessoa FEZ: horas estudadas e questões resolvidas.
///
/// PORTA SEPARADA DO ÍNDICE, e a separação é a razão de ela existir. O índice é derivado e descartável —
/// a documentação manda apagá-lo e reindexar quando algo está estranho. Isto aqui não é reconstruível a
/// partir de nada: uma série temporal perdida está perdida, e nenhuma varredura de disco a traz de volta.
///
/// É a terceira categoria de dado do sistema, definida no docs/NORTE.md:
///
///   conhecimento → arquivo .md      (perder custa REAPRENDER)
///   registro     → banco durável    (perder custa NÃO SABER MAIS o que aconteceu)   ← este
///   derivado     → banco descartável (perder custa esperar um reindex)
///
/// Por consequência, ele TEM DE ENTRAR NO BACKUP. Sem isso, a promessa "o vault é seu" passa a valer só
/// para metade do que a pessoa construiu.
/// </summary>
public interface IRegistroDeEstudo
{
    Task RegistrarQuestoesAsync(LoteDeQuestoes lote, CancellationToken ct = default);

    Task RegistrarSessaoAsync(SessaoDeEstudo sessao, CancellationToken ct = default);

    /// <summary>Os lotes do período, do mais recente para o mais antigo.</summary>
    Task<IReadOnlyList<LoteDeQuestoes>> QuestoesAsync(DateTimeOffset desde, CancellationToken ct = default);

    Task<IReadOnlyList<SessaoDeEstudo>> SessoesAsync(DateTimeOffset desde, CancellationToken ct = default);
}
