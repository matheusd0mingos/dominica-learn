using System.Globalization;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Painel;

/// <summary>Uma nota com prazo: o caminho e a data que ela mesma declara no frontmatter.</summary>
public sealed record PrazoDaNota(CaminhoNota Nota, DateOnly Data);

/// <summary>
/// PRAZOS NO FRONTMATTER — a primeira propriedade "rica" do vault, e a de mais valor para concurso:
/// inscrição que fecha, recurso que expira, edital que sai. A nota escreve
///
///     ---
///     prazo: 2026-09-01
///     ---
///
/// e o painel passa a cobrar. A data mora NA NOTA, seguindo a regra do projeto (conhecimento no .md):
/// o Obsidian mostra a mesma propriedade, e reindexar do zero não perde prazo nenhum.
///
/// Dois formatos de data, e não "o que o parse aceitar": ISO (2026-09-01) e brasileiro (01/09/2026).
/// Aceitar formato ambíguo — 03/04 é março ou abril? — seria gravar silenciosamente a data errada, e
/// prazo errado é pior que prazo recusado.
/// </summary>
public static class Prazos
{
    /// <summary>A chave no frontmatter. Minúscula, como as demais do projeto (favorito, referencia).</summary>
    public const string Chave = "prazo";

    /// <summary>A data declarada, ou nulo quando não há prazo (ou o texto não é uma data que se entenda).</summary>
    public static DateOnly? De(Frontmatter frontmatter)
    {
        ArgumentNullException.ThrowIfNull(frontmatter);
        var texto = frontmatter.Texto(Chave)?.Trim();
        if (string.IsNullOrEmpty(texto)) return null;

        string[] formatos = ["yyyy-MM-dd", "dd/MM/yyyy"];
        return DateOnly.TryParseExact(texto, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    /// <summary>
    /// O que o painel diz de cada prazo: "é hoje", "em N dia(s)", "venceu há N dia(s)". Texto pronto no
    /// domínio para a tela e o teste dizerem exatamente a mesma coisa.
    /// </summary>
    public static string Rotulo(DateOnly prazo, DateOnly hoje)
    {
        var dias = prazo.DayNumber - hoje.DayNumber;
        return dias switch
        {
            0 => "é hoje",
            1 => "amanhã",
            > 1 => $"em {dias} dias",
            -1 => "venceu ontem",
            _ => $"venceu há {-dias} dias",
        };
    }

    /// <summary>
    /// Ordena para a tela: vencidos primeiro (são os que doem), depois os mais próximos. Prazo distante
    /// demais (mais de um ano) fica fora — é anotação de arquivo, não cobrança de painel.
    /// </summary>
    public static IReadOnlyList<PrazoDaNota> ParaOPainel(IEnumerable<PrazoDaNota> prazos, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(prazos);
        return [.. prazos
            .Where(p => p.Data.DayNumber - hoje.DayNumber <= 366)
            .OrderBy(p => p.Data)
            .ThenBy(p => p.Nota.Valor, StringComparer.CurrentCultureIgnoreCase)];
    }
}
