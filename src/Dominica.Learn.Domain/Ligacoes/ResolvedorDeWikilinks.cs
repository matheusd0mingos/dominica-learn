using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Ligacoes;

/// <summary>Uma nota como o resolvedor precisa conhecê-la: onde está e por que nomes atende.</summary>
public sealed record NotaConhecida(CaminhoNota Caminho, IReadOnlyList<string> Apelidos)
{
    public static NotaConhecida De(CaminhoNota caminho) => new(caminho, Array.Empty<string>());
}

/// <summary>
/// Transforma "[[Licitações]]" no caminho de uma nota de verdade.
///
/// POR QUE ISTO É UMA CLASSE, E NÃO UM MÉTODO: resolver exige conhecer o vault INTEIRO — o mesmo texto
/// resolve para caminhos diferentes conforme quais notas existem. Construir os índices uma vez e resolver
/// milhares de ligações contra eles é a diferença entre reindexar em segundos e reindexar em minutos.
///
/// A ORDEM DE PREFERÊNCIA (a mesma do Obsidian, e a única que não surpreende quem vem de lá):
///   1. caminho exato a partir da raiz do vault      [[Direito/Licitações]]
///   2. sufixo de caminho                            [[Direito/Licitações]] acha Concursos/Direito/Licitações.md
///   3. nome do arquivo                              [[Licitações]]
///   4. apelido declarado no frontmatter             aliases: [Lei 14.133]
///
/// EMPATE É RESOLVIDO POR PROXIMIDADE, DEPOIS POR ORDEM ALFABÉTICA. A proximidade é o que faz
/// "[[Índice]]" dentro de Direito/ apontar para Direito/Índice.md e não para Física/Índice.md. A ordem
/// alfabética no fim não é estética: sem um critério final determinístico, duas reindexações do MESMO
/// vault produziriam grafos diferentes, e backlink que pisca é backlink em que ninguém confia.
/// </summary>
public sealed class ResolvedorDeWikilinks
{
    private readonly Dictionary<string, CaminhoNota> _porCaminho;
    private readonly Dictionary<string, List<CaminhoNota>> _porNome;
    private readonly Dictionary<string, List<CaminhoNota>> _porApelido;

    public ResolvedorDeWikilinks(IEnumerable<NotaConhecida> notas)
    {
        _porCaminho = new Dictionary<string, CaminhoNota>(StringComparer.OrdinalIgnoreCase);
        _porNome = new Dictionary<string, List<CaminhoNota>>(StringComparer.OrdinalIgnoreCase);
        _porApelido = new Dictionary<string, List<CaminhoNota>>(StringComparer.OrdinalIgnoreCase);

        foreach (var nota in notas)
        {
            _porCaminho[nota.Caminho.Valor] = nota.Caminho;
            _porCaminho[SemExtensao(nota.Caminho.Valor)] = nota.Caminho;
            Acrescentar(_porNome, nota.Caminho.Nome, nota.Caminho);
            foreach (var apelido in nota.Apelidos)
                Acrescentar(_porApelido, apelido, nota.Caminho);
        }
    }

    public ResolvedorDeWikilinks(IEnumerable<CaminhoNota> caminhos)
        : this(caminhos.Select(NotaConhecida.De)) { }

    /// <summary>Resolve todas as ligações de uma nota. Ligações externas e internas não viram destino.</summary>
    public IReadOnlyList<LigacaoResolvida> Resolver(CaminhoNota origem, IEnumerable<Wikilink> ligacoes)
    {
        var resolvidas = new List<LigacaoResolvida>();
        foreach (var l in ligacoes)
        {
            if (l.EhExterna) continue;
            resolvidas.Add(new LigacaoResolvida
            {
                Origem = origem,
                Alvo = l.Alvo,
                Destino = l.Alvo.Length == 0 ? null : Resolver(l.Alvo, origem),
                Secao = l.Secao,
                Rotulo = l.Rotulo,
                Forma = l.Forma,
                Posicao = l.Posicao,
            });
        }
        return resolvidas;
    }

    /// <summary>Resolve um alvo isolado. <paramref name="origem"/> desempata por proximidade.</summary>
    public CaminhoNota? Resolver(string alvo, CaminhoNota? origem = null)
    {
        if (string.IsNullOrWhiteSpace(alvo)) return null;
        var limpo = alvo.Trim().Replace('\\', '/').TrimStart('/');

        // 1) caminho exato (com ou sem .md)
        if (_porCaminho.TryGetValue(limpo, out var exato)) return exato;
        if (_porCaminho.TryGetValue(SemExtensao(limpo), out var exatoSemExt)) return exatoSemExt;

        // 2) sufixo de caminho — "[[Direito/Licitações]]" acha "Concursos/Direito/Licitações.md"
        if (limpo.Contains('/'))
        {
            var sufixo = "/" + SemExtensao(limpo);
            var porSufixo = _porCaminho.Values.Distinct()
                .Where(c => SemExtensao(c.Valor).EndsWith(sufixo, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (porSufixo.Count > 0) return Desempatar(porSufixo, origem);
        }

        // 3) nome do arquivo
        var nome = SemExtensao(limpo);
        var barra = nome.LastIndexOf('/');
        if (barra >= 0) nome = nome[(barra + 1)..];
        if (_porNome.TryGetValue(nome, out var porNome) && porNome.Count > 0) return Desempatar(porNome, origem);

        // 4) apelido declarado no frontmatter
        if (_porApelido.TryGetValue(limpo, out var porApelido) && porApelido.Count > 0) return Desempatar(porApelido, origem);

        return null;
    }

    /// <summary>
    /// Entre candidatos, vence quem compartilha mais pastas com a origem; empatou, vence a ordem
    /// alfabética do caminho — critério arbitrário, mas ESTÁVEL, que é o que importa.
    /// </summary>
    private static CaminhoNota Desempatar(List<CaminhoNota> candidatos, CaminhoNota? origem)
    {
        if (candidatos.Count == 1) return candidatos[0];
        return candidatos
            .OrderByDescending(c => origem is null ? 0 : PastasEmComum(c, origem))
            .ThenBy(c => c.Valor, StringComparer.Ordinal)
            .First();
    }

    private static int PastasEmComum(CaminhoNota a, CaminhoNota b)
    {
        var sa = a.Segmentos;
        var sb = b.Segmentos;
        var n = 0;
        while (n < sa.Count && n < sb.Count && string.Equals(sa[n], sb[n], StringComparison.OrdinalIgnoreCase)) n++;
        return n;
    }

    private static string SemExtensao(string caminho) =>
        caminho.EndsWith(CaminhoNota.Extensao, StringComparison.OrdinalIgnoreCase)
            ? caminho[..^CaminhoNota.Extensao.Length]
            : caminho;

    private static void Acrescentar(Dictionary<string, List<CaminhoNota>> mapa, string chave, CaminhoNota caminho)
    {
        if (!mapa.TryGetValue(chave, out var lista)) mapa[chave] = lista = [];
        lista.Add(caminho);
    }
}
