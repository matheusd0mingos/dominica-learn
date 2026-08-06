namespace Dominica.Learn.Domain.Analise;

/// <summary>
/// O bloco de metadados entre "---" no topo da nota.
///
/// ESTE PARSER É UM SUBCONJUNTO DECLARADO DE YAML, NÃO YAML. Suporta:
///   chave: valor escalar
///   chave: [a, b, c]          (lista em linha)
///   chave:                    (lista em bloco)
///     - a
///     - b
///
/// Não suporta mapas aninhados, âncoras, multi-linha (| e >), tipos, nem aspas com escape complexo.
///
/// POR QUE NÃO PUXAR UM YAML DE VERDADE: o domínio não deve depender de biblioteca de serialização — é a
/// regra da arquitetura hexagonal, e aqui ela paga. O que o Learn precisa do frontmatter é o punhado de
/// chaves que muda o COMPORTAMENTO (title, tags, aliases); o resto é metadado do usuário, preservado
/// intacto no arquivo e simplesmente não interpretado. Um parser completo traria 100% do YAML para
/// resolver 5% do uso, e o dia em que o subconjunto não bastar, esta classe vira uma porta e o adaptador
/// usa a biblioteca que quiser — sem que uma linha do domínio mude.
/// </summary>
public sealed class Frontmatter
{
    public static readonly Frontmatter Vazio = new(new Dictionary<string, IReadOnlyList<string>>(), 0);

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _campos;

    /// <summary>Quantas linhas do conteúdo o bloco ocupa (incluindo os dois "---"). 0 quando não há bloco.</summary>
    public int LinhasOcupadas { get; }

    private Frontmatter(IReadOnlyDictionary<string, IReadOnlyList<string>> campos, int linhasOcupadas)
    {
        _campos = campos;
        LinhasOcupadas = linhasOcupadas;
    }

    public bool Existe => LinhasOcupadas > 0;

    /// <summary>Primeiro valor da chave, ou null. Chave é insensível a maiúsculas.</summary>
    public string? Texto(string chave) =>
        _campos.TryGetValue(chave.ToLowerInvariant(), out var v) && v.Count > 0 ? v[0] : null;

    /// <summary>Todos os valores da chave (lista ou escalar único). Vazio quando ausente.</summary>
    public IReadOnlyList<string> Lista(string chave) =>
        _campos.TryGetValue(chave.ToLowerInvariant(), out var v) ? v : Array.Empty<string>();

    public IReadOnlyCollection<string> Chaves => (IReadOnlyCollection<string>)_campos.Keys;

    /// <summary>Lê o bloco a partir das linhas da nota. Sem bloco no topo, devolve <see cref="Vazio"/>.</summary>
    public static Frontmatter Ler(IReadOnlyList<string> linhas)
    {
        // O bloco só vale na PRIMEIRA linha. Um "---" no meio do texto é linha horizontal em Markdown, e
        // interpretá-lo como frontmatter transformaria um separador visual em metadado silenciosamente.
        if (linhas.Count == 0 || linhas[0].TrimEnd() != "---") return Vazio;

        var fim = -1;
        for (var i = 1; i < linhas.Count; i++)
        {
            var t = linhas[i].TrimEnd();
            if (t is "---" or "...") { fim = i; break; }
        }
        // Bloco aberto e nunca fechado não é frontmatter — é um traço solto. Tratar como metadado comeria
        // a nota inteira.
        if (fim < 0) return Vazio;

        var campos = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        string? chaveAberta = null;
        List<string>? listaAberta = null;

        void Fechar()
        {
            if (chaveAberta is not null && listaAberta is not null && listaAberta.Count > 0)
                campos[chaveAberta] = listaAberta;
            chaveAberta = null;
            listaAberta = null;
        }

        for (var i = 1; i < fim; i++)
        {
            var linha = linhas[i];
            if (linha.Trim().Length == 0) continue;

            // item de lista em bloco: "  - valor"
            var semIndentacao = linha.TrimStart();
            if (chaveAberta is not null && semIndentacao.StartsWith("- ", StringComparison.Ordinal))
            {
                listaAberta ??= [];
                listaAberta.Add(LimparEscalar(semIndentacao[2..]));
                continue;
            }

            var doisPontos = linha.IndexOf(':');
            if (doisPontos <= 0) { Fechar(); continue; }

            Fechar();
            var chave = linha[..doisPontos].Trim().ToLowerInvariant();
            if (chave.Length == 0) continue;
            var resto = linha[(doisPontos + 1)..].Trim();

            if (resto.Length == 0)
            {
                // pode ser o início de uma lista em bloco — as próximas linhas dirão
                chaveAberta = chave;
                listaAberta = [];
            }
            else if (resto.StartsWith('[') && resto.EndsWith(']'))
            {
                var itens = resto[1..^1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(LimparEscalar)
                    .Where(x => x.Length > 0)
                    .ToArray();
                campos[chave] = itens;
            }
            else
            {
                campos[chave] = new[] { LimparEscalar(resto) };
            }
        }
        Fechar();

        return new Frontmatter(campos, fim + 1);
    }

    private static string LimparEscalar(string bruto)
    {
        var t = bruto.Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
            t = t[1..^1];
        return t.Trim();
    }
}
