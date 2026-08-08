using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Grafo;

/// <summary>Uma matéria no mapa: quantas notas ela tem e com quantas outras ela conversa.</summary>
public sealed record MateriaNoMapa(Materia Materia, int Notas, int Vizinhas);

/// <summary>
/// Duas matérias ligadas por citações que cruzam de uma para a outra.
/// <see cref="Peso"/> = quantos PARES DE NOTAS cruzam — ver o comentário em <see cref="GrafoDeMaterias"/>.
/// </summary>
public sealed record PonteEntreMaterias(int De, int Para, int Peso);

/// <summary>
/// O MAPA DAS MATÉRIAS — o topo do drill: quais matérias conversam entre si.
///
/// POR QUE ELE EXISTE: o "vault inteiro" mostra todas as notas, e é justamente o nível que não escala —
/// com centenas de notas ele vira um pôster. O que escala é descer por camadas: matérias → uma matéria
/// → uma nota. Este é o primeiro degrau, e ele responde uma pergunta que nenhuma outra tela responde:
/// as PONTES. "Administrativo puxa Constitucional" é o tipo de relação que a pasta separou e a prova
/// cobra junto — hoje ela só aparece como um aviso de rodapé no recorte; aqui ela é o desenho principal.
///
/// —— AS DECISÕES ————————————————————————————————————————————————————————————————————
///
/// O PESO DA PONTE CONTA PARES DE NOTAS, não citações. Cinco pares de notas diferentes cruzando é uma
/// ponte mais larga que um par que se cita cinco vezes: a primeira é um RELACIONAMENTO entre os
/// assuntos, a segunda pode ser uma nota prolixa. É a mesma régua do <see cref="GrafoDeEtiquetas"/>
/// (alcance, não prolixidade) — e é a mesma conta do aviso "N ligações apontam para fora" do recorte,
/// então os dois números nunca discordam na tela.
///
/// "SEM MATÉRIA" É UM NÓ, quando há notas na raiz. Escondê-las faria o mapa mentir sobre o vault — e
/// elas são exatamente o conteúdo que ainda não foi arquivado, que é informação e não ausência dela.
///
/// LIGAÇÃO INTERNA NÃO VIRA PONTE. "Direito conversa com Direito" é verdade vazia; o que este mapa tem
/// a dizer é o cruzamento. O tamanho do nó já carrega o "quanto existe ali dentro".
///
/// A CLASSE É PURA: recebe o grafo de notas já montado, devolve a agregação. Nada de índice, nada de
/// disco — e por isso o teste dela não precisa de dublê nenhum.
/// </summary>
public static class GrafoDeMaterias
{
    public static (IReadOnlyList<MateriaNoMapa> Nos, IReadOnlyList<PonteEntreMaterias> Pontes) Montar(
        GrafoDoVault grafo)
    {
        ArgumentNullException.ThrowIfNull(grafo);
        if (grafo.Nos.Count == 0) return ([], []);

        // ORDEM ALFABÉTICA, com "Sem matéria" por último — a mesma ordem estável que decide as cores no
        // grafo de notas. Sem um critério final determinístico, o mapa trocaria de desenho sozinho.
        var materias = grafo.Nos
            .Select(n => n.Materia)
            .Distinct()
            .OrderBy(m => m.Existe ? 0 : 1)
            .ThenBy(m => m.Nome, StringComparer.Ordinal)
            .ToList();

        var indice = materias.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
        var notasPorMateria = new int[materias.Count];
        foreach (var no in grafo.Nos) notasPorMateria[indice[no.Materia]]++;

        var pontes = new Dictionary<(int, int), int>();
        foreach (var aresta in grafo.Arestas)
        {
            var de = indice[grafo.Nos[aresta.De].Materia];
            var para = indice[grafo.Nos[aresta.Para].Materia];
            if (de == para) continue;   // interna: verdade vazia, ver o resumo

            // Cada par UMA vez, menor índice primeiro — sem direção, como nas etiquetas: "A cita B" e
            // "B cita A" são a mesma ponte quando o que se pergunta é "esses assuntos conversam?".
            var chave = de < para ? (de, para) : (para, de);
            pontes[chave] = pontes.GetValueOrDefault(chave) + 1;
        }

        var vizinhas = new int[materias.Count];
        foreach (var ((a, b), _) in pontes) { vizinhas[a]++; vizinhas[b]++; }

        var nos = materias
            .Select((m, i) => new MateriaNoMapa(m, notasPorMateria[i], vizinhas[i]))
            .ToList();

        var arestas = pontes
            .OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2)
            .Select(p => new PonteEntreMaterias(p.Key.Item1, p.Key.Item2, p.Value))
            .ToList();

        return (nos, arestas);
    }
}
