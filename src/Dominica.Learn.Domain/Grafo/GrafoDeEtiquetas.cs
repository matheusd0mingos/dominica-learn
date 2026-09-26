using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Grafo;

/// <summary>Uma etiqueta no mapa: em quantas notas ela aparece e com quantas outras ela divide nota.</summary>
public sealed record EtiquetaNoGrafo(Etiqueta Etiqueta, int Notas, int Vizinhas);

/// <summary>Duas etiquetas que aparecem juntas. <see cref="Peso"/> = em quantas notas.</summary>
public sealed record ParDeEtiquetas(int De, int Para, int Peso);

/// <summary>
/// O MAPA DAS ETIQUETAS: quem anda com quem.
///
/// POR QUE É UM GRAFO SEPARADO, e não etiqueta virando ponto no grafo de notas: são duas perguntas
/// diferentes, e misturá-las daria um mapa com dois tipos de bicho dentro. O grafo de NOTAS responde
/// "isto aponta para aquilo" — é hierarquia, e cada ponto tem conteúdo. Este responde "isto costuma
/// aparecer junto com aquilo" — é vizinhança, e etiqueta não tem conteúdo nenhum, é um rótulo.
///
/// A ARESTA É COOCORRÊNCIA: duas etiquetas se ligam quando dividem uma nota, e o peso é em quantas.
/// Não há direção — "#prescricao anda com #tributario" é a mesma frase ao contrário —, e é por isso
/// que o par é guardado uma vez só, sempre com o menor índice primeiro.
///
/// O QUE ISTO RESPONDE, e o grafo de notas não responde: você etiqueta "#decorar" por meses e um dia
/// vê que ele quase sempre aparece junto de "#tributario". Isso não é uma ligação que alguém escreveu
/// — ninguém escreveu — é um padrão que só existe no conjunto. É a diferença entre o mapa do que você
/// ligou e o mapa do que você faz sem perceber.
///
/// A CLASSE É PURA: recebe as etiquetas de cada nota, devolve o mapa. Nada de índice, nada de disco.
/// </summary>
public static class GrafoDeEtiquetas
{
    /// <param name="porNota">
    /// As etiquetas de cada nota, uma lista por nota. Repetição da mesma etiqueta dentro de uma nota é
    /// ignorada: escrever "#direito" três vezes num texto não é três notas, e contá-las inflaria o
    /// tamanho do ponto por prolixidade em vez de por alcance.
    /// </param>
    public static (IReadOnlyList<EtiquetaNoGrafo> Nos, IReadOnlyList<ParDeEtiquetas> Pares) Montar(
        IEnumerable<IReadOnlyList<Etiqueta>> porNota)
    {
        ArgumentNullException.ThrowIfNull(porNota);

        var notas = porNota.Select(e => e.Distinct().ToList()).Where(e => e.Count > 0).ToList();

        // A ORDEM É ALFABÉTICA, e não por frequência. Não é estética: sem um critério final
        // determinístico, duas montagens do MESMO vault produziriam mapas diferentes — e um mapa que
        // muda de desenho sozinho destrói a memória espacial, que é a única razão de um grafo existir.
        var todas = notas.SelectMany(e => e).Distinct()
            .OrderBy(e => e.Valor, StringComparer.Ordinal).ToList();

        var indice = todas.Select((e, i) => (e, i)).ToDictionary(x => x.e, x => x.i);
        var quantasNotas = new int[todas.Count];
        var pares = new Dictionary<(int, int), int>();

        foreach (var etiquetas in notas)
        {
            foreach (var e in etiquetas) quantasNotas[indice[e]]++;

            // Cada par UMA VEZ, sempre com o menor índice primeiro. Sem isso, "#a com #b" e "#b com #a"
            // virariam duas arestas sobre a mesma dupla e a linha sairia com o dobro da grossura.
            for (var i = 0; i < etiquetas.Count; i++)
                for (var j = i + 1; j < etiquetas.Count; j++)
                {
                    var a = indice[etiquetas[i]];
                    var b = indice[etiquetas[j]];
                    var chave = a < b ? (a, b) : (b, a);
                    pares[chave] = pares.GetValueOrDefault(chave) + 1;
                }
        }

        var vizinhas = new int[todas.Count];
        foreach (var ((a, b), _) in pares) { vizinhas[a]++; vizinhas[b]++; }

        var nos = todas
            .Select((e, i) => new EtiquetaNoGrafo(e, quantasNotas[i], vizinhas[i]))
            .ToList();

        var arestas = pares
            .OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2)
            .Select(p => new ParDeEtiquetas(p.Key.Item1, p.Key.Item2, p.Value))
            .ToList();

        return (nos, arestas);
    }
}
