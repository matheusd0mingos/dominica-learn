namespace Dominica.Learn.Domain.Analise;

/// <summary>
/// Um nó do painel de etiquetas.
///
/// <see cref="Proprias"/> são as notas marcadas com ESTA etiqueta exata; <see cref="Total"/> inclui as
/// que estão marcadas com qualquer descendente. As duas aparecem porque respondem perguntas diferentes:
/// "#direito (48)" diz o tamanho da disciplina, e "#direito — 3 notas direto aqui" denuncia as três que
/// ficaram sem tópico.
/// </summary>
public sealed record NoDeEtiqueta(
    Etiqueta Etiqueta,
    string Rotulo,
    int Proprias,
    int Total,
    IReadOnlyList<NoDeEtiqueta> Filhos);

/// <summary>
/// Monta o painel hierárquico de etiquetas a partir das etiquetas de cada nota.
///
/// POR QUE A ENTRADA É "AS ETIQUETAS DE CADA NOTA", e não a lista de etiquetas já contada — que seria
/// a consulta óbvia e mais barata:
///
/// Porque o total de um nó pai NÃO é a soma dos filhos. Uma nota marcada com <c>#direito</c> E
/// <c>#direito/penal</c> — que é como se marca de verdade, primeiro a disciplina e depois o tópico —
/// seria contada duas vezes em <c>#direito</c>. O número inflado não quebra nada, não dá erro, e é
/// exatamente por isso que é perigoso: ele vira a medida que a pessoa usa para decidir o que estudar.
/// Contando nota a nota, com as etiquetas dela juntas, o problema não existe: cada nota entra uma vez
/// em cada ancestral, por construção.
///
/// A CLASSE É PURA. Ela não sabe o que é banco, usuário ou tela — recebe listas de etiquetas e devolve
/// uma árvore. É o que permite testar o caso da nota com pai e filho sem subir um Postgres.
/// </summary>
public static class ArvoreDeEtiquetas
{
    public static IReadOnlyList<NoDeEtiqueta> Montar(IEnumerable<IReadOnlyList<Etiqueta>> etiquetasPorNota)
    {
        ArgumentNullException.ThrowIfNull(etiquetasPorNota);

        // Chave insensível a caixa, como a própria Etiqueta: quem escreve "#Direito" numa nota e
        // "#direito" noutra quer a mesma gaveta.
        var proprias = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totais = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var canonica = new Dictionary<string, Etiqueta>(StringComparer.OrdinalIgnoreCase);

        foreach (var daNota in etiquetasPorNota)
        {
            if (daNota is null) continue;

            // Dentro de UMA nota, cada etiqueta e cada ancestral contam uma vez só — a nota que repete
            // "#direito" em três parágrafos não é três notas.
            var vistasNestaNota = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var propriasDestaNota = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var etiqueta in daNota)
            {
                if (etiqueta is null) continue;

                if (propriasDestaNota.Add(etiqueta.Valor))
                {
                    canonica.TryAdd(etiqueta.Valor, etiqueta);
                    proprias[etiqueta.Valor] = proprias.GetValueOrDefault(etiqueta.Valor) + 1;
                }

                foreach (var ancestral in etiqueta.ComAncestrais())
                {
                    canonica.TryAdd(ancestral.Valor, ancestral);
                    if (vistasNestaNota.Add(ancestral.Valor))
                        totais[ancestral.Valor] = totais.GetValueOrDefault(ancestral.Valor) + 1;
                }
            }
        }

        return Filhos(canonica, proprias, totais, prefixo: []);
    }

    private static IReadOnlyList<NoDeEtiqueta> Filhos(
        Dictionary<string, Etiqueta> canonica,
        Dictionary<string, int> proprias,
        Dictionary<string, int> totais,
        IReadOnlyList<string> prefixo)
    {
        var nivel = prefixo.Count;

        return canonica.Values
            .Where(e => e.Segmentos.Count == nivel + 1 && ComecaCom(e, prefixo))
            .Select(e => new NoDeEtiqueta(
                e,
                // O rótulo é só o ÚLTIMO segmento: numa árvore indentada, repetir
                // "direito/administrativo/licitações" em cada nível empurra o texto para fora da tela e
                // esconde justamente o que distingue um irmão do outro.
                e.Segmentos[^1],
                proprias.GetValueOrDefault(e.Valor),
                totais.GetValueOrDefault(e.Valor),
                Filhos(canonica, proprias, totais, e.Segmentos)))
            // Maior primeiro: a disciplina em que a pessoa mais escreveu é a que ela mais consulta.
            // Empate resolvido por nome, para a ordem não dançar entre uma visita e outra.
            .OrderByDescending(n => n.Total)
            .ThenBy(n => n.Rotulo, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool ComecaCom(Etiqueta etiqueta, IReadOnlyList<string> prefixo)
    {
        for (var i = 0; i < prefixo.Count; i++)
            if (!string.Equals(etiqueta.Segmentos[i], prefixo[i], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }
}
