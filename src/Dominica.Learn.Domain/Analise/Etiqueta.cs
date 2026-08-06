namespace Dominica.Learn.Domain.Analise;

/// <summary>
/// Uma etiqueta (tag), possivelmente hierárquica: "#direito/administrativo/licitações".
///
/// A hierarquia não é enfeite: quem estuda para concurso organiza por disciplina → tópico → subtópico, e
/// precisa perguntar "tudo de #direito" e receber também o que está marcado só como #direito/penal. Por
/// isso a etiqueta guarda os SEGMENTOS, e não uma string opaca — a consulta por ancestral é uma operação
/// do domínio, não um LIKE improvisado na camada de dados.
/// </summary>
public sealed record Etiqueta
{
    /// <summary>Texto sem o "#", com os separadores originais. Ex.: "direito/administrativo".</summary>
    public string Valor { get; }

    /// <summary>Segmentos da hierarquia, de fora para dentro.</summary>
    public IReadOnlyList<string> Segmentos { get; }

    private Etiqueta(string valor, IReadOnlyList<string> segmentos)
    {
        Valor = valor;
        Segmentos = segmentos;
    }

    /// <summary>Aceita com ou sem "#". Devolve null quando não é uma etiqueta válida.</summary>
    public static Etiqueta? TentarCriar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return null;

        var texto = bruto.Trim();
        if (texto.StartsWith('#')) texto = texto[1..];
        texto = texto.Trim('/');
        if (texto.Length == 0) return null;

        var segmentos = texto.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segmentos.Length == 0) return null;

        foreach (var s in segmentos)
            if (!EhSegmentoValido(s)) return null;

        return new Etiqueta(string.Join('/', segmentos), segmentos);
    }

    /// <summary>
    /// Um segmento vale se tiver ao menos um caractere que não seja dígito.
    ///
    /// A regra existe por causa de "#1", "#5" e "#2026": numeração e ano aparecem o tempo todo em nota de
    /// estudo, e sem esta regra cada um viraria etiqueta até o painel de etiquetas deixar de servir para
    /// organizar. É a mesma regra do Obsidian — e vale notar o que ela NÃO cobre: "#fff" é etiqueta válida
    /// aqui e lá, porque tem letras. Código de cor em nota real está dentro de bloco de código, e é o
    /// analisador que o descarta, não esta regra.
    /// </summary>
    private static bool EhSegmentoValido(string s)
    {
        var temNaoDigito = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || c == '#') return false;
            if (!char.IsDigit(c)) temNaoDigito = true;
        }
        return temNaoDigito;
    }

    /// <summary>Esta etiqueta é a própria <paramref name="outra"/> ou está abaixo dela na hierarquia?</summary>
    public bool EstaAbaixoDe(Etiqueta outra)
    {
        if (outra.Segmentos.Count > Segmentos.Count) return false;
        for (var i = 0; i < outra.Segmentos.Count; i++)
            if (!string.Equals(Segmentos[i], outra.Segmentos[i], StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>Todas as etiquetas ancestrais, incluindo ela mesma: #a/b/c → #a, #a/b, #a/b/c.</summary>
    public IEnumerable<Etiqueta> ComAncestrais()
    {
        for (var i = 1; i <= Segmentos.Count; i++)
            yield return new Etiqueta(string.Join('/', Segmentos.Take(i)), Segmentos.Take(i).ToArray());
    }

    // Etiqueta é INSENSÍVEL a maiúsculas (ao contrário do caminho da nota): quem digita "#Direito" e
    // "#direito" quer a mesma gaveta, e o sistema de arquivos não tem opinião sobre isso.
    public bool Equals(Etiqueta? outra) => outra is not null && string.Equals(Valor, outra.Valor, StringComparison.OrdinalIgnoreCase);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Valor);
    public override string ToString() => $"#{Valor}";
}
