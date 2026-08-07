using System.Globalization;
using System.Text;

namespace Dominica.Learn.Domain.Vault;

/// <summary>
/// A pontuação de um candidato. Quanto maior, mais acima ele aparece.
/// </summary>
public sealed record AcertoPorNome(string Texto, int Pontos, IReadOnlyList<int> Posicoes);

/// <summary>
/// Busca por nome do jeito que um abridor rápido precisa: DIGITAÇÃO PARCIAL, fora de ordem de teclas
/// mas em ordem de letras — "dirtrib" acha "Direito tributário", "licit" acha "Licitações".
///
/// POR QUE ISTO NÃO É UM `Contains`:
///
/// Um abridor rápido só substitui a lista se ele acertar com POUCAS TECLAS. Com `Contains`, achar
/// "Direito tributário" exige digitar "direito trib" inteiro — mais do que rolar a lista, e aí ninguém
/// usa. O que faz a ferramenta valer é casar subsequência: as letras na ordem, com buracos no meio.
///
/// E POR QUE ELE PONTUA, em vez de só filtrar: quem digita "cr" quer "Crase", não "Direito
/// Administrativo/Contratos e Recursos". Sem ordenação, o certo aparece em décimo e a pessoa desiste.
/// A pontuação premia, nesta ordem: começo de palavra, letras coladas, e o nome do arquivo acima da
/// pasta — porque é o nome que a pessoa está tentando lembrar.
///
/// ACENTO NÃO PODE ATRAPALHAR. Quem procura "portugues" tem de achar "Português": exigir o acento numa
/// busca rápida é exigir que a pessoa pare para lembrar da grafia justamente quando ela está com
/// pressa. A comparação é feita sobre o texto sem acento, mas o que se mostra é o original.
/// </summary>
public static class BuscaPorNome
{
    /// <summary>
    /// Ordena os candidatos pelo quanto casam com <paramref name="termo"/>. Quem não casa fica de fora.
    /// Termo vazio devolve tudo na ordem em que veio — é o estado de quem abriu o buscador e ainda não
    /// digitou, e ali a ordem que o chamador escolheu (recentes primeiro) é a melhor que existe.
    /// </summary>
    public static IReadOnlyList<AcertoPorNome> Ordenar(string? termo, IEnumerable<string> candidatos)
    {
        ArgumentNullException.ThrowIfNull(candidatos);

        var busca = SemAcento(termo?.Trim() ?? string.Empty);
        if (busca.Length == 0)
            return candidatos.Select(c => new AcertoPorNome(c, 0, [])).ToList();

        var acertos = new List<AcertoPorNome>();
        foreach (var candidato in candidatos)
        {
            var acerto = Pontuar(busca, candidato);
            if (acerto is not null) acertos.Add(acerto);
        }

        return acertos
            .OrderByDescending(a => a.Pontos)
            // Empate desfeito pelo MENOR texto: entre "Crase" e "Crase e acentuação", quem digitou
            // "crase" quase sempre quer o primeiro.
            .ThenBy(a => a.Texto.Length)
            .ThenBy(a => a.Texto, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Pontua um candidato. Null quando as letras do termo não aparecem, na ordem, dentro dele.
    /// </summary>
    private static AcertoPorNome? Pontuar(string busca, string candidato)
    {
        var alvo = SemAcento(candidato);
        var posicoes = new List<int>(busca.Length);

        var pontos = 0;
        var anterior = -2;
        var i = 0;

        for (var b = 0; b < busca.Length; b++)
        {
            var achou = -1;
            while (i < alvo.Length)
            {
                if (alvo[i] == busca[b]) { achou = i; i++; break; }
                i++;
            }
            if (achou < 0) return null;   // faltou letra: não é candidato

            posicoes.Add(achou);
            pontos += 1;

            // Letra colada na anterior: a pessoa digitou um pedaço contínuo do nome, o que é sinal
            // forte de que é ISTO que ela quer.
            if (achou == anterior + 1) pontos += 5;

            // Começo de palavra (ou do texto, ou depois de barra): "dt" achando "Direito tributário"
            // é acerto muito melhor do que "dt" caindo no meio de duas palavras quaisquer.
            if (achou == 0 || EhSeparador(alvo[achou - 1])) pontos += 8;

            anterior = achou;
        }

        // O NOME DO ARQUIVO vale mais que a pasta. Quem digita "licit" está lembrando do nome da nota,
        // não do caminho até ela — e sem este peso, uma pasta chamada "Licitações" com dez notas
        // dentro empurraria a nota "Licitações" para o décimo lugar.
        var barra = alvo.LastIndexOf('/');
        if (barra >= 0 && posicoes[0] > barra) pontos += 15;

        // Casamento curto num texto curto vale mais: "crase" em "Crase" é acerto perfeito; a mesma
        // busca dentro de um caminho de oitenta caracteres é coincidência.
        pontos += Math.Max(0, 20 - alvo.Length / 4);

        return new AcertoPorNome(candidato, pontos, posicoes);
    }

    private static bool EhSeparador(char c) => c is ' ' or '/' or '-' or '_' or '.';

    /// <summary>
    /// Tira acento e caixa. "Português" → "portugues". Decompor e jogar fora os diacríticos é o que
    /// faz "portugues" achar "Português" sem uma tabela de substituições para manter.
    /// </summary>
    private static string SemAcento(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
