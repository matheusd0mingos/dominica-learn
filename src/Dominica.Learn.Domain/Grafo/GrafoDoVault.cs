using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Grafo;

/// <summary>Uma nota no grafo. <see cref="Grau"/> = quantas ligações tocam nela (entrando ou saindo).</summary>
public sealed record NoDoGrafo(CaminhoNota Caminho, string Titulo, int Grau, bool Orfa, Materia Materia);

/// <summary>Uma ligação entre dois nós, por índice na lista de nós. <see cref="Peso"/> = quantas vezes.</summary>
public sealed record ArestaDoGrafo(int De, int Para, int Peso);

/// <summary>
/// O grafo de uma matéria só, mais quantas ligações ficaram do lado de fora.
///
/// <see cref="LigacoesParaFora"/> não é estatística de enfeite: recortar uma matéria ESCONDE as pontes
/// que ela tem com as outras, e esconder sem avisar faz o recorte parecer o vault inteiro. Numa base de
/// estudo essas pontes são o achado mais valioso que existe — "isto de Administrativo puxa Constitucional"
/// é exatamente o tipo de ligação que a prova cobra e que a pasta separou.
/// </summary>
public sealed record RecorteDeMateria(GrafoDoVault Grafo, int LigacoesParaFora);

/// <summary>
/// O grafo de conhecimento: quem aponta para quem.
///
/// É a visão que o Obsidian tornou famosa, e o motivo dela existir não é estética — é diagnóstico. Duas
/// perguntas que só o grafo responde, e que importam muito em estudo de longo prazo:
///   • que assuntos viraram um EMARANHADO (grau alto demais) e precisam ser quebrados em notas menores?
///   • que notas estão ÓRFÃS — escritas e nunca ligadas a nada, portanto nunca revisitadas?
///
/// A segunda é a mais valiosa e a mais ignorada: nota órfã é conhecimento que você produziu e perdeu.
/// Por isso <see cref="NoDoGrafo.Orfa"/> é campo de primeira classe, e não algo para o olho descobrir.
/// </summary>
public sealed class GrafoDoVault
{
    public IReadOnlyList<NoDoGrafo> Nos { get; }
    public IReadOnlyList<ArestaDoGrafo> Arestas { get; }

    private GrafoDoVault(IReadOnlyList<NoDoGrafo> nos, IReadOnlyList<ArestaDoGrafo> arestas)
    {
        Nos = nos;
        Arestas = arestas;
    }

    public static readonly GrafoDoVault Vazio = new([], []);

    public IEnumerable<NoDoGrafo> Orfas => Nos.Where(n => n.Orfa);

    /// <summary>
    /// As matérias presentes, em ordem estável. A ordem é o que decide a cor de cada uma na tela —
    /// ordenar por nome é o que faz "Direito" continuar da mesma cor entre uma abertura e outra.
    /// </summary>
    public IReadOnlyList<Materia> Materias => Nos
        .Select(n => n.Materia)
        .Distinct()
        .OrderBy(m => m.Nome, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Monta o grafo. <paramref name="titulos"/> é opcional; sem ele o rótulo é o nome do arquivo.
    ///
    /// Ligações QUEBRADAS não viram nós: a nota que ainda não existe não é conhecimento ainda, e desenhar
    /// um ponto para cada link não escrito encheria o grafo de fantasmas — justo o oposto do que ele
    /// serve para mostrar. Elas aparecem na lista "ainda por escrever", que é onde são úteis.
    /// </summary>
    public static GrafoDoVault Montar(
        IEnumerable<CaminhoNota> caminhos,
        IEnumerable<LigacaoResolvida> ligacoes,
        IReadOnlyDictionary<CaminhoNota, string>? titulos = null)
    {
        // ordem estável: o layout depende dela, e grafo que reorganiza a cada carregamento é grafo que
        // ninguém consegue reconhecer de um dia para o outro
        var ordenados = caminhos.Distinct().OrderBy(c => c.Valor, StringComparer.Ordinal).ToList();
        var indice = new Dictionary<CaminhoNota, int>();
        for (var i = 0; i < ordenados.Count; i++) indice[ordenados[i]] = i;

        var pesos = new Dictionary<(int, int), int>();
        var grau = new int[ordenados.Count];

        foreach (var l in ligacoes)
        {
            if (l.Destino is null || l.EhInterna) continue;
            if (!indice.TryGetValue(l.Origem, out var de)) continue;
            if (!indice.TryGetValue(l.Destino, out var para)) continue;
            if (de == para) continue;   // autolink não diz nada sobre a rede

            var chave = (Math.Min(de, para), Math.Max(de, para));
            pesos[chave] = pesos.TryGetValue(chave, out var p) ? p + 1 : 1;
            grau[de]++;
            grau[para]++;
        }

        var nos = ordenados.Select((c, i) => new NoDoGrafo(
            c,
            titulos is not null && titulos.TryGetValue(c, out var t) && t.Length > 0 ? t : c.Nome,
            grau[i],
            grau[i] == 0,
            // A matéria sai do CAMINHO, que já está aqui. Buscá-la em outro lugar custaria uma leitura de
            // disco por nota só para desenhar o grafo — num vault de anos, centenas delas por abertura.
            Materia.De(c))).ToList();

        var arestas = pesos
            .OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2)
            .Select(p => new ArestaDoGrafo(p.Key.Item1, p.Key.Item2, p.Value))
            .ToList();

        return new GrafoDoVault(nos, arestas);
    }

    /// <summary>
    /// O grafo de UMA matéria: só as notas dela e as ligações entre elas.
    ///
    /// É um recorte de verdade, e não um esmaecimento das outras: assim o layout é recalculado só para
    /// estas notas e usa a tela inteira. A estrutura interna de "Direito Administrativo" aparece do
    /// tamanho que ela é, em vez de espremida num canto pelo resto do vault.
    ///
    /// O grau de cada nó é RECALCULADO dentro do recorte. Manter o grau original faria uma nota parecer
    /// central por causa de ligações que não estão desenhadas — e, pior, esconderia a órfã-dentro-da-
    /// matéria: a nota que só se liga a outra matéria e não conversa com nenhuma colega de pasta. Essa é
    /// justamente uma das coisas que este recorte serve para mostrar.
    /// </summary>
    public RecorteDeMateria DaMateria(Materia materia)
    {
        var mantidos = Nos.Select((n, i) => (n, i)).Where(x => x.n.Materia == materia).Select(x => x.i).ToList();
        if (mantidos.Count == 0) return new RecorteDeMateria(Vazio, 0);

        var remapa = mantidos.Select((antigo, novo) => (antigo, novo)).ToDictionary(x => x.antigo, x => x.novo);

        var dentro = new List<ArestaDoGrafo>();
        var paraFora = 0;
        foreach (var a in Arestas)
        {
            var temDe = remapa.ContainsKey(a.De);
            var temPara = remapa.ContainsKey(a.Para);
            if (temDe && temPara) dentro.Add(new ArestaDoGrafo(remapa[a.De], remapa[a.Para], a.Peso));
            else if (temDe || temPara) paraFora++;
        }

        var grau = new int[mantidos.Count];
        foreach (var a in dentro) { grau[a.De] += a.Peso; grau[a.Para] += a.Peso; }

        var nos = mantidos
            .Select((antigo, novo) => Nos[antigo] with { Grau = grau[novo], Orfa = grau[novo] == 0 })
            .ToList();

        return new RecorteDeMateria(new GrafoDoVault(nos, dentro), paraFora);
    }

    /// <summary>
    /// Subgrafo em volta de uma nota, até <paramref name="saltos"/> de distância.
    ///
    /// Existe porque o grafo INTEIRO de um vault de anos é uma nuvem ilegível — bonita de printar, inútil
    /// de usar. A pergunta que se faz na prática é local: "o que cerca ISTO que estou estudando agora?".
    /// </summary>
    public GrafoDoVault Vizinhanca(CaminhoNota centro, int saltos = 1)
    {
        var origem = Nos.Select((n, i) => (n, i)).FirstOrDefault(x => x.n.Caminho.Equals(centro));
        if (origem.n is null) return Vazio;

        var visiveis = new HashSet<int> { origem.i };
        var fronteira = new List<int> { origem.i };
        for (var salto = 0; salto < Math.Max(1, saltos); salto++)
        {
            var proxima = new List<int>();
            foreach (var a in Arestas)
            {
                if (fronteira.Contains(a.De) && visiveis.Add(a.Para)) proxima.Add(a.Para);
                if (fronteira.Contains(a.Para) && visiveis.Add(a.De)) proxima.Add(a.De);
            }
            if (proxima.Count == 0) break;
            fronteira = proxima;
        }

        var mantidos = visiveis.OrderBy(i => i).ToList();
        var remapa = mantidos.Select((antigo, novo) => (antigo, novo)).ToDictionary(x => x.antigo, x => x.novo);
        var nos = mantidos.Select(i => Nos[i]).ToList();
        var arestas = Arestas
            .Where(a => remapa.ContainsKey(a.De) && remapa.ContainsKey(a.Para))
            .Select(a => new ArestaDoGrafo(remapa[a.De], remapa[a.Para], a.Peso))
            .ToList();

        return new GrafoDoVault(nos, arestas);
    }
}
