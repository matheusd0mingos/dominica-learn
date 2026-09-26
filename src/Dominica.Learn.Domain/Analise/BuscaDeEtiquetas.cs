using System.Globalization;
using System.Text;

namespace Dominica.Learn.Domain.Analise;

/// <summary>Uma etiqueta candidata do autocompletar, com quantas notas já a usam.</summary>
public sealed record AcertoDeEtiqueta(Etiqueta Etiqueta, int Notas, int Pontos);

/// <summary>
/// Qual etiqueta oferecer para o que a pessoa acabou de digitar depois do "#".
///
/// O PROBLEMA QUE ISTO RESOLVE NÃO É ECONOMIZAR TECLADO — é impedir que o vault se despedace em
/// sinônimos. Sem uma lista à vista, ninguém lembra se marcou #pegadinha, #pegadinhas ou #pegadinha-cespe
/// três semanas atrás; marca-se a variação nova, e a partir daí cada uma tem um pedaço do assunto. O
/// estrago é silencioso: nada dá erro, a tela de etiquetas fica cheia, e nenhuma delas responde a
/// pergunta inteira. Uma lista com o que JÁ EXISTE é o que faz a pessoa reusar em vez de inventar.
///
/// POR QUE A CONTAGEM ENTRA NA ORDENAÇÃO, e não só o quanto o texto casa: entre duas etiquetas que casam
/// igual, a que já tem quarenta notas é quase sempre a que a pessoa quer, e a que tem uma é justamente a
/// variação errada que escapou numa tarde. Ordenar por uso empurra o vault de volta para o vocabulário
/// que ele já tem — que é o efeito que se quer.
///
/// CASA POR SEGMENTO, e não só pelo começo do texto todo. Quem digita "#trib" está pensando em
/// tributário, e a etiqueta é "#direito/tributário": exigir que ele digitasse "direito/" antes seria
/// exigir que ele soubesse de cor a hierarquia que a lista existe para lembrá-lo.
///
/// SEM ACENTO E SEM CAIXA, pelo mesmo motivo do abridor rápido: parar para lembrar se foi "tributário"
/// ou "tributario" é o custo que faz a pessoa desistir e digitar de novo à mão.
/// </summary>
public static class BuscaDeEtiquetas
{
    // Os pesos existem para uma decisão só: quando "trib" casa com "#tributário" (começo) e com
    // "#direito/tributário" (começo de um segmento de dentro), o de fora vem primeiro. Os degraus são
    // largos de propósito — a contagem de notas desempata DENTRO do degrau, nunca atravessa dois.
    private const int PontosExato = 1000;
    private const int PontosComecoDoValor = 500;
    private const int PontosComecoDeSegmento = 300;
    private const int PontosContem = 100;

    /// <summary>
    /// Ordena as candidatas pelo quanto casam com <paramref name="termo"/>; quem não casa fica de fora.
    ///
    /// Termo VAZIO devolve as mais usadas, e não a lista alfabética: quem acabou de digitar "#" ainda não
    /// tem um assunto em mente, e o vocabulário que ele mais repete é o palpite mais útil que existe.
    /// </summary>
    public static IReadOnlyList<AcertoDeEtiqueta> Ordenar(
        string? termo, IEnumerable<(Etiqueta Etiqueta, int Notas)> candidatas)
    {
        ArgumentNullException.ThrowIfNull(candidatas);

        var busca = Dobrar(termo?.Trim().TrimStart('#') ?? string.Empty);

        if (busca.Length == 0)
            return candidatas
                .Select(c => new AcertoDeEtiqueta(c.Etiqueta, c.Notas, 0))
                .OrderByDescending(a => a.Notas)
                .ThenBy(a => a.Etiqueta.Valor, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        var acertos = new List<AcertoDeEtiqueta>();
        foreach (var (etiqueta, notas) in candidatas)
        {
            var pontos = Pontuar(busca, etiqueta);
            if (pontos > 0) acertos.Add(new AcertoDeEtiqueta(etiqueta, notas, pontos));
        }

        return acertos
            .OrderByDescending(a => a.Pontos)
            .ThenByDescending(a => a.Notas)
            // Último desempate pelo MENOR valor: entre "#direito" e "#direito/tributário/prescrição",
            // quem digitou "direito" pediu o de fora. E é estável — a mesma digitação devolve sempre a
            // mesma ordem, o que importa porque a escolha é feita com a seta, de olho fechado.
            .ThenBy(a => a.Etiqueta.Valor.Length)
            .ThenBy(a => a.Etiqueta.Valor, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static int Pontuar(string busca, Etiqueta etiqueta)
    {
        var valor = Dobrar(etiqueta.Valor);

        if (valor == busca) return PontosExato;
        if (valor.StartsWith(busca, StringComparison.Ordinal)) return PontosComecoDoValor;

        // Começo de QUALQUER segmento: "trib" acha "direito/tributário". O primeiro segmento já foi
        // coberto acima, mas repeti-lo aqui não muda nada e evita um caso especial.
        foreach (var segmento in etiqueta.Segmentos)
            if (Dobrar(segmento).StartsWith(busca, StringComparison.Ordinal))
                return PontosComecoDeSegmento;

        // Por último, no meio de qualquer lugar. Vale pouco de propósito: é o que salva quem lembra do
        // fim da palavra e não do começo, sem deixar esse caso empurrar os outros para baixo.
        return valor.Contains(busca, StringComparison.Ordinal) ? PontosContem : 0;
    }

    /// <summary>
    /// Tira acento e caixa. Mesma função do resto do domínio, e pelo mesmo motivo — ver BuscaPorNome.
    /// </summary>
    private static string Dobrar(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
