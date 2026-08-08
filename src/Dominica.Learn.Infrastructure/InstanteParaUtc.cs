using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dominica.Learn.Infrastructure;

/// <summary>
/// TODO DateTimeOffset ENTRA NO POSTGRES EM UTC — e esta conversão é a fronteira que garante isso.
///
/// A regressão que ela impede de voltar (achada na auditoria de deploy, minutos depois do relógio do
/// produto passar a emitir o fuso de Brasília): o Npgsql RECUSA DateTimeOffset com offset diferente de
/// zero em "timestamp with time zone" — a exceção estourava dentro do ErrorBoundary e o painel ficava
/// um minuto em branco. O produto pensa em Brasília (ver IRelogio); o banco fala UTC; a conversão mora
/// AQUI, na borda, e não espalhada em ToUniversalTime() por cada consulta — que foi exatamente o tipo
/// de espalhamento que deixou o `.ToLocalTime()` errado passar despercebido por meses.
///
/// Na volta não se converte nada: timestamptz já sai do Postgres em UTC (offset zero), e quem precisa
/// do dia local converte com o fuso do relógio (ver MapaDeCalor/MapaDeHoras).
/// </summary>
public sealed class InstanteParaUtc() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    v => v.ToUniversalTime(), v => v);
