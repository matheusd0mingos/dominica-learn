using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Grafo;

/// <summary>
/// Uma etiqueta oferecida a uma nota.
///
/// <see cref="PorCoocorrencia"/> separa duas coisas que pareceriam iguais na tela e não são: quando é
/// coocorrência, <see cref="Peso"/> é "em quantas notas ela divide espaço com o que esta nota já tem";
/// quando não é, é "em quantas notas do vault ela aparece". Mostrar os dois números com a mesma frase
/// faria a tela mentir sobre o que ela sabe.
/// </summary>
public sealed record EtiquetaSugerida(Etiqueta Etiqueta, int Peso, bool PorCoocorrencia);

/// <summary>
/// O QUE OFERECER A UMA NOTA, a partir do que o vault inteiro já mostra.
///
/// É A COOCORRÊNCIA SERVIDA ONDE ELA VIRA AÇÃO. A conta é a mesma que desenha o mapa das etiquetas, mas
/// o mapa responde "o que anda junto no meu vault" — pergunta de fim de semana. A do dia é outra:
/// "marquei #tributário nesta nota; o que eu costumo marcar junto e esqueci agora?".
///
/// POR QUE É DOMÍNIO, e não três linhas dentro do serviço: isto é uma REGRA — o que conta como parente,
/// como se ordena, o que fazer quando não há parente nenhum. Regra sem teste é regra que muda sozinha, e
/// dentro do serviço ela só seria testável com dublê de índice. Aqui é uma função pura sobre o grafo que
/// o <see cref="GrafoDeEtiquetas"/> já monta.
///
/// —— AS DUAS DECISÕES ————————————————————————————————————————————————————————————————
///
/// SOMA OS PESOS, em vez de contar vizinhas. É o que faz "#decorar, que divide 8 notas com #tributário"
/// ganhar de "#teclado, que dividiu uma nota com ela uma vez". Contar vizinhas empataria as duas em 1, e
/// a lista sairia ordenada por acaso.
///
/// COMPLETA COM AS MAIS USADAS quando a coocorrência não enche a lista, em vez de devolver menos. O
/// buraco que isto tapa apareceu no navegador: uma nota cujas etiquetas são exclusivas dela não coocorre
/// com nada, e o bloco de sugestão sumia inteiro — na nota mais isolada do vault, que é justamente a que
/// mais precisa de um caminho de volta.
/// </summary>
public static class SugestaoDeEtiquetas
{
    /// <param name="nos">As etiquetas do vault, como o grafo as montou.</param>
    /// <param name="pares">As coocorrências, com peso = em quantas notas as duas dividem espaço.</param>
    /// <param name="jaTem">As etiquetas que a nota já tem. Elas nunca são oferecidas de volta.</param>
    public static IReadOnlyList<EtiquetaSugerida> Para(
        IReadOnlyList<EtiquetaNoGrafo> nos,
        IReadOnlyList<ParDeEtiquetas> pares,
        IReadOnlyCollection<Etiqueta> jaTem,
        int limite = 6)
    {
        ArgumentNullException.ThrowIfNull(nos);
        ArgumentNullException.ThrowIfNull(pares);
        ArgumentNullException.ThrowIfNull(jaTem);

        if (nos.Count == 0 || limite <= 0) return [];

        var tem = jaTem.ToHashSet();

        var peso = new Dictionary<Etiqueta, int>();
        foreach (var p in pares)
        {
            var a = nos[p.De].Etiqueta;
            var b = nos[p.Para].Etiqueta;

            if (tem.Contains(a) && !tem.Contains(b)) peso[b] = peso.GetValueOrDefault(b) + p.Peso;
            else if (tem.Contains(b) && !tem.Contains(a)) peso[a] = peso.GetValueOrDefault(a) + p.Peso;
        }

        var sugeridas = peso
            .OrderByDescending(x => x.Value)
            // DESEMPATE ALFABÉTICO, e não a ordem do dicionário: sem ele, duas aberturas da MESMA nota
            // ofereceriam as mesmas etiquetas em ordens diferentes, e a mão nunca aprenderia onde clicar.
            .ThenBy(x => x.Key.Valor, StringComparer.Ordinal)
            .Take(limite)
            .Select(x => new EtiquetaSugerida(x.Key, x.Value, PorCoocorrencia: true))
            .ToList();

        if (sugeridas.Count >= limite) return sugeridas;

        var jaOferecidas = sugeridas.Select(s => s.Etiqueta).ToHashSet();
        sugeridas.AddRange(nos
            // "n.Notas > 1" corta as usadas UMA vez só: oferecer a etiqueta que escapou uma vez, sem
            // nenhum parentesco com esta nota, é ruído com cara de sugestão.
            .Where(n => n.Notas > 1 && !tem.Contains(n.Etiqueta) && !jaOferecidas.Contains(n.Etiqueta))
            .OrderByDescending(n => n.Notas)
            .ThenBy(n => n.Etiqueta.Valor, StringComparer.Ordinal)
            .Take(limite - sugeridas.Count)
            .Select(n => new EtiquetaSugerida(n.Etiqueta, n.Notas, PorCoocorrencia: false)));

        return sugeridas;
    }
}
