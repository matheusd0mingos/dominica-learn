using System.Globalization;
using System.Text;

namespace Dominica.Learn.Domain.Vault;

/// <summary>
/// A NOTA DIÁRIA — a espinha dorsal da captura, direto da filosofia do Obsidian.
///
/// A ideia solta que aparece no meio de outra coisa morre se não tiver onde cair AGORA: decidir pasta,
/// título e matéria é atrito suficiente para "depois eu anoto" — e depois não existe. A nota diária é o
/// chão que está sempre lá: uma nota por dia, criada no primeiro uso, onde tudo entra com hora e sem
/// cerimônia. Organizar é depois; capturar é já.
///
/// Uma pasta "Diário" com o nome do dia em ISO (2026-08-08.md): ordena sozinha em qualquer listagem,
/// inclusive no Obsidian. A CLASSE É PURA — recebe a data e a hora de quem chama; quem sabe as horas é
/// a aplicação.
/// </summary>
public static class NotaDiaria
{
    /// <summary>Onde os dias moram. "Diário" — é o nome que a pessoa procuraria no Obsidian.</summary>
    public const string Pasta = "Diário";

    public static CaminhoNota CaminhoDe(DateOnly dia) =>
        CaminhoNota.De($"{Pasta}/{dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.md");

    /// <summary>
    /// O título diz o dia por extenso — "sexta-feira, 08/08/2026". Escrito à mão, sem depender da
    /// cultura do servidor: o app é pt-BR onde quer que rode (mesma regra do DiaCurto do painel).
    /// </summary>
    public static string ConteudoInicial(DateOnly dia) =>
        $"# {NomeDoDia(dia.DayOfWeek)}, {dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}\n";

    /// <summary>
    /// Acrescenta uma captura ao fim da nota: um item de lista com a hora. Multilinha vira continuação
    /// INDENTADA do mesmo item — uma captura é UMA ideia, não uma ideia mais dois parágrafos soltos.
    /// </summary>
    public static string Capturar(string conteudo, TimeOnly hora, string texto)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        if (string.IsNullOrWhiteSpace(texto)) return conteudo;

        var linhas = texto.Trim().Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(conteudo.TrimEnd());
        sb.Append('\n');

        // Linha em branco antes só quando o que vem acima NÃO é outra captura: capturas seguidas formam
        // uma lista contínua; depois do título (ou de texto comum), a linha em branco separa o bloco.
        if (!UltimaLinhaEhItem(conteudo)) sb.Append('\n');

        sb.Append("- **").Append(hora.ToString("HH:mm", CultureInfo.InvariantCulture)).Append("** — ")
          .Append(linhas[0].Trim());
        foreach (var linha in linhas.Skip(1))
            sb.Append('\n').Append("  ").Append(linha.TrimEnd());

        sb.Append('\n');
        return sb.ToString();
    }

    private static bool UltimaLinhaEhItem(string conteudo)
    {
        // A continuação indentada ("  texto") também conta como item — é o corpo da captura anterior.
        var ultima = conteudo.TrimEnd().Split('\n')[^1];
        return ultima.TrimStart().StartsWith("- ", StringComparison.Ordinal)
            || ultima.TrimStart().StartsWith("* ", StringComparison.Ordinal)
            || ultima.StartsWith("  ", StringComparison.Ordinal);
    }

    private static string NomeDoDia(DayOfWeek dia) => dia switch
    {
        DayOfWeek.Monday => "segunda-feira",
        DayOfWeek.Tuesday => "terça-feira",
        DayOfWeek.Wednesday => "quarta-feira",
        DayOfWeek.Thursday => "quinta-feira",
        DayOfWeek.Friday => "sexta-feira",
        DayOfWeek.Saturday => "sábado",
        _ => "domingo",
    };
}
