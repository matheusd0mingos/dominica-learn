using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Grafo;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Templates;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

/// <summary>Um template disponível: a nota que serve de molde.</summary>
public sealed record TemplateDisponivel(CaminhoNota Caminho, string Nome);

/// <summary>Uma matéria e quantas notas ela tem — o que o painel lateral e a legenda do grafo mostram.</summary>
public sealed record MateriaContada(Materia Materia, int Notas);

/// <summary>
/// O que se faz com o conhecimento DEPOIS que ele está escrito: ver renderizado, navegar o grafo, marcar
/// favorito, criar a partir de um molde.
///
/// Separado de <see cref="ServicoDeNotas"/> de propósito. Aquele guarda a invariante da ESCRITA (toda
/// gravação reindexa, toda leitura sabe que o disco pode ter mudado). Este é de LEITURA e organização —
/// só o favorito escreve, e escreve pelo serviço de notas para não duplicar a invariante. Juntar os dois
/// daria uma classe de trinta métodos que ninguém lê inteira.
/// </summary>
public sealed class ServicoDeConhecimento(
    IRepositorioDeNotas repositorio,
    IIndiceDoVault indice,
    IRenderizadorDeMarkdown renderizador,
    IArmazemDeAnexos anexos,
    ServicoDeNotas notas,
    IRelogio relogio,
    ILogger<ServicoDeConhecimento> log)
{
    /// <summary>
    /// Pasta onde ficam os templates. É uma pasta do VAULT, não configuração da aplicação: template é
    /// conhecimento do usuário — ele escreveu aquele roteiro de resumo — e sobrevive a reindexar,
    /// sincroniza, e abre no Obsidian como qualquer outra nota.
    /// </summary>
    public const string PastaDeTemplates = "Templates";

    // —— RENDERIZAÇÃO ————————————————————————————————————————————————————————————————
    public async Task<NotaRenderizada> RenderizarAsync(string markdown, CancellationToken ct = default)
    {
        var conhecidas = await indice.NotasConhecidasAsync(ct);
        var resolvedor = new ResolvedorDeWikilinks(conhecidas);
        return renderizador.Renderizar(markdown, alvo => resolvedor.Resolver(alvo), UrlDoAnexo);
    }

    /// <summary>
    /// "foto.png" → "/anexos/Anexos/foto.png", ou null se o arquivo não está no vault.
    ///
    /// A consulta é síncrona porque o renderizador é síncrono, e o renderizador é síncrono porque
    /// converter texto em texto não tem por que não ser. O custo real é um <c>File.Exists</c> por embed —
    /// e só por embed, nunca por wikilink de nota.
    /// </summary>
    private string? UrlDoAnexo(string referencia)
    {
        var caminho = anexos.ResolverAsync(referencia).GetAwaiter().GetResult();
        return caminho is null ? null : "/anexos/" + string.Join('/', caminho.Valor.Split('/').Select(Uri.EscapeDataString));
    }

    // —— FAVORITOS ————————————————————————————————————————————————————————————————————
    /// <summary>
    /// Alterna o favorito ESCREVENDO NO FRONTMATTER da nota.
    ///
    /// Poderia ser uma coluna no Postgres e seria mais fácil. Seria também a violação exata da regra que
    /// rege o projeto: favorito é conhecimento do usuário, e conhecimento do usuário não pode sumir num
    /// "reindexar do zero" nem ser invisível quando ele abre o vault no Obsidian.
    /// </summary>
    public async Task<Resultado<bool>> AlternarFavoritoAsync(CaminhoNota caminho, CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado<bool>.NaoEncontrada($"A nota \"{caminho}\"");

        var novoEstado = !EditorDeFrontmatter.EhFavorita(nota.Analise);
        var conteudo = EditorDeFrontmatter.DefinirFavorito(nota.Conteudo, novoEstado);

        // passa pelo serviço de notas: ele é quem arquiva no histórico e reindexa
        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        return salva.Ok
            ? Resultado<bool>.Sucesso(novoEstado)
            : Resultado<bool>.Falha(salva.Motivo ?? MotivoDaFalha.Invalida, salva.Mensagem ?? "Não consegui marcar.");
    }

    public async Task<IReadOnlyList<NotaIndexada>> FavoritasAsync(CancellationToken ct = default)
    {
        // O índice não guarda "favorito" como coluna (de propósito: ele é derivado). A leitura passa pelo
        // vault. Em vault grande isto vai ficar caro e a resposta certa será um índice DERIVADO do
        // frontmatter — derivado, e portanto reconstruível, que é diferente de ser a fonte.
        var caminhos = await indice.TodosOsCaminhosAsync(ct);
        var favoritas = new List<NotaIndexada>();
        foreach (var caminho in caminhos)
        {
            var nota = await repositorio.LerAsync(caminho, ct);
            if (nota is null || !EditorDeFrontmatter.EhFavorita(nota.Analise)) continue;
            favoritas.Add(new NotaIndexada(nota.Caminho, nota.Titulo, nota.Analise.Resumo,
                nota.ModificadoEm, nota.Analise.Palavras, nota.Analise.Etiquetas, nota.Analise.Apelidos));
        }
        return favoritas.OrderBy(f => f.Titulo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// A nota é conteúdo de estudo, e não ferramenta?
    ///
    /// Hoje a única exceção é a pasta de templates. Ela está numa função só, e não repetida em cada
    /// consulta, porque a primeira versão a filtrava na lista de matérias e esquecia no grafo — e o
    /// resultado era um "Templates (1)" na legenda do grafo que a lista ao lado jurava não existir. Duas
    /// telas discordando sobre o que é uma matéria.
    /// </summary>
    private static bool EhConteudoDeEstudo(CaminhoNota caminho) =>
        !caminho.Pasta.Equals(PastaDeTemplates, StringComparison.OrdinalIgnoreCase);

    // —— MATÉRIAS ————————————————————————————————————————————————————————————————————
    /// <summary>
    /// As matérias do vault, com a contagem de notas de cada uma.
    ///
    /// Sai dos CAMINHOS, que o índice já tem — nenhuma leitura de disco e nenhuma tabela nova. A matéria
    /// é a primeira pasta, então a lista se atualiza sozinha no instante em que uma nota é movida, sem
    /// nada para reindexar.
    ///
    /// A pasta de templates fica de fora: ela é ferramenta, não conteúdo de estudo, e apareceria no topo
    /// da lista de matérias competindo com Direito e Português.
    /// </summary>
    public async Task<IReadOnlyList<MateriaContada>> MateriasAsync(CancellationToken ct = default)
    {
        var caminhos = await indice.TodosOsCaminhosAsync(ct);
        return caminhos
            .Where(EhConteudoDeEstudo)
            .GroupBy(Materia.De)
            .Select(g => new MateriaContada(g.Key, g.Count()))
            // Sem matéria por último: é a caixa de entrada do vault, não uma matéria de verdade, e no
            // topo empurraria as reais para baixo justamente quando há muita coisa por arquivar.
            .OrderBy(m => m.Materia.Existe ? 0 : 1)
            .ThenBy(m => m.Materia.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // —— TEMPLATES ————————————————————————————————————————————————————————————————————
    public async Task<IReadOnlyList<TemplateDisponivel>> TemplatesAsync(CancellationToken ct = default)
    {
        var caminhos = await indice.TodosOsCaminhosAsync(ct);
        return caminhos
            .Where(c => c.Pasta.Equals(PastaDeTemplates, StringComparison.OrdinalIgnoreCase))
            .Select(c => new TemplateDisponivel(c, c.Nome))
            .OrderBy(t => t.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Cria uma nota a partir de um template, com as variáveis já substituídas.</summary>
    public async Task<Resultado<Nota>> CriarDeTemplateAsync(
        CaminhoNota template, CaminhoNota destino, string? autor = null, CancellationToken ct = default)
    {
        var molde = await repositorio.LerAsync(template, ct);
        if (molde is null) return Resultado<Nota>.NaoEncontrada($"O template \"{template}\"");

        var conteudo = AplicadorDeTemplate.Aplicar(molde.Conteudo,
            new ContextoDoTemplate(destino.Nome, relogio.Agora, autor));

        log.LogInformation("Nota {Destino} criada a partir do template {Template}.", destino, template);
        return await notas.CriarAsync(destino, conteudo, ct);
    }

    // —— GRAFO ————————————————————————————————————————————————————————————————————————
    /// <summary>
    /// Monta o grafo do vault (ou a vizinhança de uma nota) já com as posições calculadas.
    ///
    /// <paramref name="centro"/> nulo = vault inteiro. Numa base de anos isso vira uma nuvem ilegível —
    /// por isso o uso normal da tela é com centro, respondendo "o que cerca ISTO?", que é a pergunta que
    /// alguém faz de verdade enquanto estuda.
    /// </summary>
    public async Task<GrafoPosicionado> GrafoAsync(
        CaminhoNota? centro = null, int saltos = 2, double largura = 900, double altura = 620,
        CancellationToken ct = default)
    {
        // Mesmo recorte da lista de matérias: template é ferramenta, não conhecimento, e um nó "{{titulo}}"
        // no grafo é ruído que nunca vai se ligar a nada.
        var caminhos = (await indice.TodosOsCaminhosAsync(ct)).Where(EhConteudoDeEstudo).ToList();
        if (caminhos.Count == 0) return LayoutDeForca.Calcular(GrafoDoVault.Vazio, largura, altura);

        var ligacoes = new List<LigacaoResolvida>();
        var titulos = new Dictionary<CaminhoNota, string>();
        foreach (var c in caminhos)
        {
            ligacoes.AddRange(await indice.LigacoesDeAsync(c, ct));
            if (await indice.ObterAsync(c, ct) is { } n) titulos[c] = n.Titulo;
        }

        var grafo = GrafoDoVault.Montar(caminhos, ligacoes, titulos);
        if (centro is not null) grafo = grafo.Vizinhanca(centro, saltos);

        return LayoutDeForca.Calcular(grafo, largura, altura);
    }
}
