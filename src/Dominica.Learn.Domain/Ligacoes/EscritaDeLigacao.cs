using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Ligacoes;

/// <summary>
/// Como escrever um <c>[[link]]</c> novo para uma nota.
///
/// PARECE DETALHE E NÃO É. Quem completa um link está escrevendo um texto que vai reler por anos, e a
/// diferença entre <c>[[Licitações]]</c> e <c>[[Direito Administrativo/Concursos/Licitações]]</c> no meio
/// de um parágrafo é a diferença entre uma frase legível e uma linha de caminho de arquivo. O padrão do
/// Obsidian é escrever o mais curto que ainda resolva, e é o que se faz aqui.
///
/// A REGRA TEM UMA CONDIÇÃO, e é ela que justifica a classe existir: a forma curta só serve enquanto o
/// nome for único no vault. No dia em que a pessoa criar uma segunda "Licitações" noutra matéria, um
/// <c>[[Licitações]]</c> escrito hoje passa a ser ambíguo — e ambiguidade em link não dá erro, ela leva
/// para a nota errada em silêncio. Por isso a decisão é tomada no momento de ESCREVER, olhando o vault
/// inteiro, e não deixada para o resolvedor adivinhar depois.
///
/// É a contraparte de <see cref="ReescritorDeLigacoes"/>: lá se preserva a forma que o autor escolheu,
/// aqui se escolhe a primeira. As duas seguem a mesma regra de propósito — se divergissem, renomear uma
/// nota reescreveria links que o autocompletar acabou de escrever.
/// </summary>
public static class EscritaDeLigacao
{
    /// <summary>
    /// O texto que vai DENTRO dos colchetes: só o nome, quando ele é único no vault; o caminho sem a
    /// extensão, quando existe outra nota com o mesmo nome.
    /// </summary>
    /// <param name="todas">
    /// O vault inteiro. Contar homônimas exige ver todo mundo — não dá para responder olhando só o alvo.
    /// </param>
    public static string MaisCurta(CaminhoNota alvo, IEnumerable<CaminhoNota> todas)
    {
        ArgumentNullException.ThrowIfNull(alvo);
        ArgumentNullException.ThrowIfNull(todas);

        var homonimas = 0;
        foreach (var candidata in todas)
        {
            // OrdinalIgnoreCase, e não Ordinal: é assim que o resolvedor de ligações casa a forma curta.
            // Se aqui fosse sensível à caixa, "licitações" e "Licitações" pareceriam nomes diferentes na
            // hora de escrever e o MESMO nome na hora de resolver — e o link sairia ambíguo sem ninguém
            // ter feito nada errado.
            if (!string.Equals(candidata.Nome, alvo.Nome, StringComparison.OrdinalIgnoreCase)) continue;
            if (++homonimas > 1) return SemExtensao(alvo.Valor);
        }

        return alvo.Nome;
    }

    private static string SemExtensao(string caminho) =>
        caminho.EndsWith(CaminhoNota.Extensao, StringComparison.OrdinalIgnoreCase)
            ? caminho[..^CaminhoNota.Extensao.Length]
            : caminho;
}
