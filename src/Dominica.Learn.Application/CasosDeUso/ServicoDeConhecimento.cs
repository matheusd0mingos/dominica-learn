using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Estudo;
using Dominica.Learn.Domain.Grafo;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Painel;
using Dominica.Learn.Domain.Templates;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

/// <summary>Um template disponível: a nota que serve de molde.</summary>
public sealed record TemplateDisponivel(CaminhoNota Caminho, string Nome);

/// <summary>Uma matéria e quantas notas ela tem — o que o painel lateral e a legenda do grafo mostram.</summary>
public sealed record MateriaContada(Materia Materia, int Notas);

/// <summary>
/// Onde a ligação foi escrita e como. A tela precisa dos três para não mentir: em qual nota mexeu, o
/// que apareceu lá dentro, e se de fato houve mudança.
/// </summary>
public sealed record LigacaoEscrita(CaminhoNota Nota, string Texto, bool JaHavia)
{
    /// <summary>A linha como ela ficou no arquivo — é isto que a mensagem mostra.</summary>
    public string Linha => $"- [[{Texto}]]";
}


/// <summary>
/// O mapa das etiquetas, pronto para desenhar: os nós, as posições e os pares.
///
/// As posições vêm do MESMO motor do grafo de notas (LayoutDeForca.Posicionar), que não sabe o que os
/// nós são. Dois mapas, um layout — e por isso um conserto no desenho vale para os dois.
/// </summary>
public sealed record MapaDeEtiquetas(
    IReadOnlyList<EtiquetaNoGrafo> Nos,
    IReadOnlyList<PosicaoDoNo> Posicoes,
    IReadOnlyList<ParDeEtiquetas> Pares,
    double Largura,
    double Altura)
{
    public static readonly MapaDeEtiquetas Vazio = new([], [], [], 800, 600);
}

/// <summary>O mapa das matérias pronto para desenhar — o topo do drill. Ver <see cref="GrafoDeMaterias"/>.</summary>
public sealed record MapaDeMaterias(
    IReadOnlyList<MateriaNoMapa> Nos,
    IReadOnlyList<PosicaoDoNo> Posicoes,
    IReadOnlyList<PonteEntreMaterias> Pontes,
    double Largura,
    double Altura)
{
    public static readonly MapaDeMaterias Vazio = new([], [], [], 800, 600);
}

/// <summary>
/// Tudo que a tela do grafo precisa numa consulta só.
///
/// <see cref="Materias"/> vem SEMPRE do recorte completo, mesmo quando <see cref="Filtrada"/> está
/// preenchida: é a legenda, e uma legenda que só listasse a matéria já escolhida tiraria da tela o único
/// caminho para trocar de matéria.
/// </summary>
public sealed record VisaoDoGrafo(
    GrafoPosicionado Posicionado,
    IReadOnlyList<MateriaContada> Materias,
    Materia? Filtrada,
    int LigacoesParaFora)
{
    public static readonly VisaoDoGrafo Vazia =
        new(new GrafoPosicionado(GrafoDoVault.Vazio, [], 0, 0), [], null, 0);
}

/// <summary>
/// O que se faz com o conhecimento DEPOIS que ele está escrito: ver renderizado, navegar o grafo, marcar
/// favorito, criar a partir de um molde.
///
/// Separado de <see cref="ServicoDeNotas"/> de propósito. Aquele guarda a invariante da ESCRITA (toda
/// gravação reindexa, toda leitura sabe que o disco pode ter mudado). Este é de LEITURA e organização —
/// só o favorito escreve, e escreve pelo serviço de notas para não duplicar a invariante. Juntar os dois
/// daria uma classe de trinta métodos que ninguém lê inteira.
/// </summary>
public sealed partial class ServicoDeConhecimento(
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

    // —— CICLO DE ESTUDOS ————————————————————————————————————————————————————————————
    //
    // O ciclo mora numa NOTA do vault (ver CicloDeEstudos) — é plano do usuário, e plano do usuário não
    // pode viver só no índice. Ler é ler a nota; gravar é gravar pelo ServicoDeNotas, que arquiva a
    // versão anterior e reindexa como em qualquer edição.

    private static CaminhoNota CaminhoDoCiclo => CaminhoNota.De(CicloDeEstudos.CaminhoDaNota);

    public async Task<IReadOnlyList<BlocoDoCiclo>> CicloAsync(CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(CaminhoDoCiclo, ct);
        return CicloDeEstudos.Ler(nota?.Conteudo);
    }

    public async Task<Resultado> SalvarCicloAsync(IReadOnlyList<BlocoDoCiclo> blocos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blocos);
        var atual = await repositorio.LerAsync(CaminhoDoCiclo, ct);
        var conteudo = CicloDeEstudos.Escrever(blocos);

        // Impressão do que está no disco: se a nota do ciclo mudou por fora (editada no Obsidian), o
        // SalvarAsync recusa em vez de atropelar. Nulo quando a nota ainda não existe — aí ele cria.
        var r = await notas.SalvarAsync(CaminhoDoCiclo, conteudo, atual?.Impressao, autor: null, ct);
        return r.Ok
            ? Resultado.Sucesso
            : Resultado.Falha(r.Motivo ?? MotivoDaFalha.Invalida, r.Mensagem ?? "Não consegui salvar o ciclo.");
    }

    // O PLANO DA SEMANA mora noutra nota do vault, pela mesma razão do ciclo: é plano do usuário. As metas
    // são propostas pela fraqueza (ver PlanoDaSemana.Propor), mas o que fica gravado é o que a pessoa aceitou.

    private static CaminhoNota CaminhoDoPlano => CaminhoNota.De(PlanoDaSemana.CaminhoDaNota);

    public async Task<IReadOnlyList<MetaDaSemana>> PlanoDaSemanaAsync(CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(CaminhoDoPlano, ct);
        return PlanoDaSemana.Ler(nota?.Conteudo);
    }

    public async Task<Resultado> SalvarPlanoAsync(IReadOnlyList<MetaDaSemana> metas, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metas);
        var atual = await repositorio.LerAsync(CaminhoDoPlano, ct);
        var conteudo = PlanoDaSemana.Escrever(metas);

        // Mesma proteção do ciclo: se a nota mudou por fora (Obsidian), recusa em vez de atropelar.
        var r = await notas.SalvarAsync(CaminhoDoPlano, conteudo, atual?.Impressao, autor: null, ct);
        return r.Ok
            ? Resultado.Sucesso
            : Resultado.Falha(r.Motivo ?? MotivoDaFalha.Invalida, r.Mensagem ?? "Não consegui salvar o plano.");
    }

    // —— A NOTA DIÁRIA E A CAPTURA RÁPIDA ————————————————————————————————————————————
    // A espinha dorsal da captura (ver NotaDiaria). A regra dos dois métodos: NUNCA falhar por a nota
    // ainda não existir — o primeiro uso do dia é que a cria, e exigir um passo de criação seria
    // devolver o atrito que a captura existe para eliminar.

    /// <summary>O caminho da nota de hoje, criada agora se ainda não existia.</summary>
    public async Task<Resultado<CaminhoNota>> NotaDeHojeAsync(CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(relogio.Agora.DateTime);
        var caminho = NotaDiaria.CaminhoDe(hoje);

        if (await repositorio.LerAsync(caminho, ct) is not null)
            return Resultado<CaminhoNota>.Sucesso(caminho);

        var criada = await notas.CriarAsync(caminho, NotaDiaria.ConteudoInicial(hoje), ct);
        return criada.Ok
            ? Resultado<CaminhoNota>.Sucesso(caminho)
            : Resultado<CaminhoNota>.Falha(criada.Motivo ?? MotivoDaFalha.Invalida,
                criada.Mensagem ?? "Não consegui criar a nota de hoje.");
    }

    /// <summary>
    /// Guarda um pensamento na nota de hoje, com a hora. É o gesto de menor cerimônia do produto:
    /// nenhuma decisão de pasta, título ou matéria — capturar é já, organizar é depois.
    /// </summary>
    public async Task<Resultado<CaminhoNota>> CapturarAsync(string texto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return Resultado<CaminhoNota>.Invalida("Escreva o pensamento antes de guardar.");

        var de = await NotaDeHojeAsync(ct);
        if (!de.Ok) return de;
        var caminho = de.Valor!;

        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado<CaminhoNota>.NaoEncontrada("A nota de hoje");

        var agora = relogio.Agora;
        var conteudo = NotaDiaria.Capturar(nota.Conteudo, TimeOnly.FromDateTime(agora.DateTime), texto);

        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        if (!salva.Ok)
            return Resultado<CaminhoNota>.Falha(salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Mensagem ?? "Não consegui guardar a captura.");

        log.LogInformation("Captura guardada na nota de hoje.");
        return Resultado<CaminhoNota>.Sucesso(caminho);
    }

    // —— PRAZOS ——————————————————————————————————————————————————————————————————————

    /// <summary>
    /// As notas com "prazo:" no frontmatter, na ordem do painel (vencidos primeiro, ver Prazos). Varre o
    /// vault como as demais leituras de painel — o custo é o mesmo do painel de cartões, e a alternativa
    /// (indexar o campo) só se paga quando doer de verdade.
    /// </summary>
    public async Task<IReadOnlyList<PrazoDaNota>> PrazosAsync(CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(relogio.Agora.DateTime);
        var achados = new List<PrazoDaNota>();

        foreach (var caminho in await indice.TodosOsCaminhosAsync(ct))
        {
            var nota = await repositorio.LerAsync(caminho, ct);
            if (nota is null) continue;
            if (Prazos.De(nota.Analise.Frontmatter) is { } data)
                achados.Add(new PrazoDaNota(caminho, data));
        }

        return Prazos.ParaOPainel(achados, hoje);
    }

    // —— A NOTA-ÍNDICE (MOC) ——————————————————————————————————————————————————————————

    /// <summary>
    /// Cria a nota-índice de uma etiqueta — o mapa do assunto, com um link por nota (ver NotaIndice).
    /// Se já existe, DEVOLVE o caminho sem tocar nela: o índice é do usuário depois que nasce, e
    /// regenerá-lo por cima apagaria a curadoria — a parte que vale.
    /// </summary>
    public async Task<Resultado<CaminhoNota>> CriarIndiceAsync(Etiqueta etiqueta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(etiqueta);
        var caminho = NotaIndice.CaminhoDe(etiqueta);

        if (await repositorio.LerAsync(caminho, ct) is not null)
            return Resultado<CaminhoNota>.Sucesso(caminho);

        var acertos = await indice.BuscarAsync(new ConsultaDeBusca { Etiqueta = etiqueta, Limite = 500 }, ct);
        var todos = await indice.TodosOsCaminhosAsync(ct);
        var links = acertos
            .Select(a => a.Nota.Caminho)
            .Where(c => c != caminho)
            .OrderBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => EscritaDeLigacao.MaisCurta(c, todos))
            .ToList();

        var criada = await notas.CriarAsync(caminho, NotaIndice.Conteudo(etiqueta, links), ct);
        if (!criada.Ok)
            return Resultado<CaminhoNota>.Falha(criada.Motivo ?? MotivoDaFalha.Invalida,
                criada.Mensagem ?? "Não consegui criar o índice.");

        log.LogInformation("Índice criado para {Etiqueta} com {Quantas} nota(s).", etiqueta, links.Count);
        return Resultado<CaminhoNota>.Sucesso(caminho);
    }

    // —— RENDERIZAÇÃO ————————————————————————————————————————————————————————————————
    public async Task<NotaRenderizada> RenderizarAsync(string markdown, CancellationToken ct = default)
    {
        var conhecidas = await indice.NotasConhecidasAsync(ct);
        var resolvedor = new ResolvedorDeWikilinks(conhecidas);

        // O CONTEÚDO DOS EMBEDS É CARREGADO ANTES de renderizar, num dicionário — porque o renderizador
        // é síncrono (texto em texto) e ler nota é I/O. É esta a divisão de trabalho da porta: quem sabe
        // ler nota entrega o conteúdo; quem sabe desenhar decide onde ele entra.
        var embutidas = await ConteudoDosEmbedsAsync(markdown, resolvedor, ct);

        return renderizador.Renderizar(markdown, alvo => resolvedor.Resolver(alvo), UrlDoAnexo,
            embutidas.Count == 0
                ? null
                : alvo => resolvedor.Resolver(alvo) is { } c ? embutidas.GetValueOrDefault(c) : null);
    }

    /// <summary>
    /// Quantas transclusões uma nota pode carregar. O teto existe porque cada embed é uma leitura de
    /// disco e uma renderização — uma nota-índice com cem embeds transformaria cada abertura numa
    /// varredura do vault. Trinta é folga para uso real; acima disso, os demais viram link, que é a
    /// degradação já conhecida.
    /// </summary>
    public const int TetoDeTransclusoes = 30;

    private async Task<Dictionary<CaminhoNota, string>> ConteudoDosEmbedsAsync(
        string markdown, ResolvedorDeWikilinks resolvedor, CancellationToken ct)
    {
        var mapa = new Dictionary<CaminhoNota, string>();
        foreach (System.Text.RegularExpressions.Match m in EmbedDeNota().Matches(markdown))
        {
            if (mapa.Count >= TetoDeTransclusoes) break;

            var interno = m.Groups["interno"].Value;
            var corte = interno.IndexOfAny(['|', '#']);
            var alvo = (corte >= 0 ? interno[..corte] : interno).Trim();

            // Só o que RESOLVE COMO NOTA é lido: "![[foto.png]]" não resolve (anexo não é nota) e segue
            // para o fluxo de anexo do renderizador sem custar uma leitura aqui.
            if (alvo.Length == 0 || resolvedor.Resolver(alvo) is not { } caminho || mapa.ContainsKey(caminho))
                continue;

            if (await repositorio.LerAsync(caminho, ct) is { } nota) mapa[caminho] = nota.Conteudo;
        }
        return mapa;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"!\[\[(?<interno>[^\]\n]+)\]\]")]
    private static partial System.Text.RegularExpressions.Regex EmbedDeNota();

    /// <summary>
    /// "foto.png" → "anexos/Anexos/foto.png", ou null se o arquivo não está no vault.
    ///
    /// SEM BARRA INICIAL, de propósito: esta URL vai para dentro de um &lt;img src&gt;, que é HTML puro.
    /// Com barra inicial ela seria relativa à raiz do domínio e ignoraria o &lt;base&gt; — a imagem
    /// sumiria de todas as notas no dia em que o Learn fosse servido sob um sub-caminho.
    ///
    /// A consulta é síncrona porque o renderizador é síncrono, e o renderizador é síncrono porque
    /// converter texto em texto não tem por que não ser. O custo real é um <c>File.Exists</c> por embed —
    /// e só por embed, nunca por wikilink de nota.
    /// </summary>
    private string? UrlDoAnexo(string referencia)
    {
        var caminho = anexos.ResolverAsync(referencia).GetAwaiter().GetResult();
        return caminho is null ? null : "anexos/" + string.Join('/', caminho.Valor.Split('/').Select(Uri.EscapeDataString));
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

    /// <summary>
    /// Cria uma matéria — que, no disco, é uma pasta com a NOTA-ÍNDICE dela dentro.
    ///
    /// POR QUE NÃO É SÓ CRIAR A PASTA: pasta vazia não sobrevive neste vault. O repositório limpa pastas
    /// que ficaram sem arquivo depois de mover ou apagar (senão a árvore do Obsidian encheria de pastas
    /// mortas), então uma matéria "vazia" evaporaria no primeiro rearranjo — e o usuário veria a matéria
    /// que ele criou sumir sozinha.
    ///
    /// A nota-índice resolve isso e ganha algo melhor de brinde: um mapa de conteúdo, no estilo que o
    /// Obsidian consagrou. É o lugar natural do plano de estudo da matéria e das ligações para os
    /// assuntos dela — e como é uma nota comum, ela aparece no grafo, entra na busca e abre no Obsidian.
    /// Guardar a lista de matérias no banco seria a alternativa, e contrariaria a regra que rege o
    /// projeto: o que só existe no banco some num "reindexar do zero".
    /// </summary>
    public async Task<Resultado<Nota>> CriarMateriaAsync(string nome, CancellationToken ct = default)
    {
        var limpo = (nome ?? string.Empty).Trim();
        if (limpo.Length == 0) return Resultado<Nota>.Invalida("Dê um nome à matéria.");

        // A matéria é a PRIMEIRA pasta, então o nome não pode ter barra: "Direito/Administrativo" seria
        // uma submatéria, e submatéria é a pasta de dentro — criada junto com a primeira nota dela.
        if (limpo.Contains('/') || limpo.Contains('\\'))
            return Resultado<Nota>.Invalida("O nome da matéria não pode ter barra. Subpastas vêm depois, dentro dela.");

        if (!CaminhoNota.TentarCriar($"{limpo}/{limpo}{CaminhoNota.Extensao}", out var caminho, out var erro) || caminho is null)
            return Resultado<Nota>.Invalida(erro ?? "Nome de matéria inválido.");

        var materia = Materia.De(limpo);

        // Já existe se QUALQUER nota mora nela — não só se a nota-índice existe. Criar por cima de uma
        // matéria que já tem conteúdo daria a impressão de ter começado do zero.
        var caminhos = await indice.TodosOsCaminhosAsync(ct);
        if (caminhos.Any(c => Materia.De(c) == materia))
            return Resultado<Nota>.JaExiste($"A matéria \"{materia.Rotulo}\"");

        log.LogInformation("Matéria {Materia} criada.", materia.Rotulo);
        return await notas.CriarAsync(caminho, ConteudoDaNotaIndice(materia), ct);
    }

    /// <summary>
    /// O caminho é a nota-índice — o "mapa" — da própria matéria: "Trabalho/Trabalho.md".
    ///
    /// É a nota que REPRESENTA a matéria, e por isso é onde moram as decisões sobre ela. Hoje há uma:
    /// se a matéria é material de consulta. Ver <see cref="AlternarReferenciaAsync"/>.
    /// </summary>
    public static bool EhNotaIndiceDaMateria(CaminhoNota caminho)
    {
        var materia = Materia.De(caminho);
        // UM segmento: Segmentos é a PASTA, sem o arquivo. "Trabalho/Trabalho.md" tem um; a raiz tem
        // zero (e nota solta não tem matéria); "Trabalho/Sub/Trabalho.md" tem dois — mesmo nome, uma
        // pasta abaixo, e não é o mapa de matéria nenhuma.
        return materia.Existe
            && caminho.Segmentos.Count == 1
            && string.Equals(caminho.Nome, materia.Nome, StringComparison.Ordinal);
    }

    // —— MAPA DAS ETIQUETAS ————————————————————————————————————————————————————————
    /// <summary>
    /// O mapa de quem anda com quem.
    ///
    /// SAI DO ÍNDICE, numa consulta só: as etiquetas já foram extraídas na indexação, então isto não lê
    /// arquivo nenhum. É o que permite a tela abrir tão rápido quanto o grafo de notas.
    /// </summary>
    public async Task<MapaDeEtiquetas> MapaDeEtiquetasAsync(
        double largura = 800, double altura = 600, CancellationToken ct = default)
    {
        var porNota = await indice.EtiquetasPorNotaAsync(ct);
        var (nos, pares) = GrafoDeEtiquetas.Montar(porNota);
        if (nos.Count == 0) return MapaDeEtiquetas.Vazio;

        // GRUPO -1 PARA TODAS: etiqueta não tem matéria, e não há aglomerado a insinuar. Quem forma
        // aglomerado aqui é a coocorrência, que já é a aresta — inventar um grupo faria o desenho
        // afirmar um parentesco que ninguém declarou.
        var posicoes = LayoutDeForca.Posicionar(
            [.. nos.Select(e => e.Etiqueta.Valor)],
            [.. nos.Select(e => e.Vizinhas)],
            [.. nos.Select(_ => -1)],
            [.. pares.Select(p => new ArestaDoGrafo(p.De, p.Para, p.Peso))],
            largura, altura);

        return new MapaDeEtiquetas(nos, posicoes, pares, largura, altura);
    }

    /// <summary>
    /// As notas que têm AS DUAS etiquetas — a resposta ao clique na linha do mapa.
    ///
    /// INTERSEÇÃO, e cada lado com a hierarquia que TODA consulta de etiqueta do produto já entende:
    /// "#direito ∩ #pegadinha" traz a nota marcada só com "#direito/penal #pegadinha". Por isso a lista
    /// pode ser MAIOR que o peso da linha clicada — a linha conta a coocorrência literal, a consulta
    /// desce a árvore — e nunca menor: tudo que fez a linha existir está aqui.
    ///
    /// A ordem é a do primeiro lado, que a busca já devolve estável.
    /// </summary>
    public async Task<IReadOnlyList<NotaIndexada>> NotasComAmbasAsync(
        Etiqueta a, Etiqueta b, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var deA = await indice.BuscarAsync(new ConsultaDeBusca { Etiqueta = a, Limite = int.MaxValue }, ct);
        var deB = await indice.BuscarAsync(new ConsultaDeBusca { Etiqueta = b, Limite = int.MaxValue }, ct);
        var caminhosDeB = deB.Select(x => x.Nota.Caminho).ToHashSet();

        return [.. deA.Select(x => x.Nota).Where(n => caminhosDeB.Contains(n.Caminho))];
    }

    /// <summary>
    /// Renomeia (ou mescla) uma etiqueta NO VAULT INTEIRO — em cada nota que a tem, no frontmatter e no
    /// texto, com a hierarquia junto. Devolve quantas notas foram reescritas.
    ///
    /// GRAVA PELO ServicoDeNotas, nota a nota: cada reescrita arquiva a versão anterior no histórico e
    /// reindexa — renomear em massa não pode ser menos seguro que editar à mão. Se uma nota falhar (ex.:
    /// editada em outra aba no meio), as demais seguem: meia mesclagem re-executável é melhor que uma
    /// mesclagem abortada no meio sem dizer onde parou.
    /// </summary>
    public async Task<Resultado<int>> RenomearEtiquetaAsync(Etiqueta de, string? paraBruta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(de);
        if (Etiqueta.TentarCriar(paraBruta) is not { } para)
            return Resultado<int>.Invalida("O novo nome não é uma etiqueta válida. Use letras, números, hífen e \"/\".");
        if (para == de) return Resultado<int>.Invalida("O novo nome é igual ao atual.");

        var acertos = await indice.BuscarAsync(new ConsultaDeBusca { Etiqueta = de, Limite = int.MaxValue }, ct);
        var mudadas = 0;
        var falhas = 0;

        foreach (var acerto in acertos)
        {
            var nota = await repositorio.LerAsync(acerto.Nota.Caminho, ct);
            if (nota is null) continue;

            var novo = RenomeadorDeEtiqueta.Renomear(nota.Conteudo, de, para);
            if (ReferenceEquals(novo, nota.Conteudo)) continue;

            var salva = await notas.SalvarAsync(acerto.Nota.Caminho, novo, nota.Impressao, autor: null, ct);
            if (salva.Ok) mudadas++;
            else
            {
                falhas++;
                log.LogWarning("Renomear #{De}: não consegui reescrever {Caminho}: {Motivo}",
                    de.Valor, acerto.Nota.Caminho, salva.Mensagem);
            }
        }

        log.LogInformation("Etiqueta #{De} → #{Para}: {Mudadas} nota(s) reescritas, {Falhas} falha(s).",
            de.Valor, para.Valor, mudadas, falhas);

        return falhas == 0
            ? Resultado<int>.Sucesso(mudadas)
            : Resultado<int>.Falha(MotivoDaFalha.Conflito,
                $"{mudadas} nota(s) reescritas, mas {falhas} não deixaram: alguém editava. Rode de novo para terminar.");
    }

    // —— ETIQUETAS DA NOTA ABERTA ——————————————————————————————————————————————————————
    /// <summary>
    /// Põe uma etiqueta na nota, escrevendo no frontmatter dela.
    ///
    /// POR QUE ISTO EXISTE, e por que não bastava o "#" no editor: o autocompletar do "#" resolve para
    /// quem já sabe que "#" é etiqueta e já está com o cursor no texto. Ele não resolve VER o que a nota
    /// tem, não resolve TIRAR, e não resolve etiquetar uma nota inteira sem escolher em que frase enfiar
    /// o rótulo. Mesma história do "[[": a sintaxe fica, mas ela não pode ser o único caminho.
    ///
    /// ACEITA TEXTO CRU e valida aqui, porque o campo da tela deixa digitar etiqueta que ainda não
    /// existe — que é o gesto mais comum de todos: a primeira nota de um assunto novo.
    /// </summary>
    public async Task<Resultado<Etiqueta>> MarcarEtiquetaAsync(
        CaminhoNota caminho, string? bruta, CancellationToken ct = default)
    {
        if (Etiqueta.TentarCriar(bruta) is not { } etiqueta)
            return Resultado<Etiqueta>.Invalida(
                "Etiqueta inválida. Ela não pode ter espaço nem ser só número — \"#lei-14133\" vale, \"#14133\" não.");

        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado<Etiqueta>.NaoEncontrada($"A nota \"{caminho}\"");

        var (conteudo, oQueHouve) = EtiquetasDaNota.Marcar(nota.Conteudo, etiqueta, nota.Analise.Etiquetas);
        if (oQueHouve == EtiquetasDaNota.Resultado.JaTinha) return Resultado<Etiqueta>.Sucesso(etiqueta);

        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        if (!salva.Ok)
            return Resultado<Etiqueta>.Falha(salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Mensagem ?? "Não consegui gravar a etiqueta.");

        log.LogInformation("Etiquetei {Nota} com {Etiqueta}.", caminho, etiqueta);
        return Resultado<Etiqueta>.Sucesso(etiqueta);
    }

    /// <summary>
    /// Tira uma etiqueta do frontmatter da nota. Etiqueta escrita no meio do texto NÃO sai por aqui —
    /// ver <see cref="EtiquetasDaNota"/>: apagá-la reescreveria a frase de alguém.
    /// </summary>
    public async Task<Resultado<bool>> DesmarcarEtiquetaAsync(
        CaminhoNota caminho, Etiqueta etiqueta, CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado<bool>.NaoEncontrada($"A nota \"{caminho}\"");

        var (conteudo, oQueHouve) = EtiquetasDaNota.Desmarcar(nota.Conteudo, etiqueta);
        if (oQueHouve == EtiquetasDaNota.Resultado.NaoTinha)
            return Resultado<bool>.Invalida(
                $"\"{etiqueta}\" está escrita no texto da nota, e não nos metadados. " +
                "Tirá-la daqui mudaria a frase — apague no editor, onde dá para ver o que some.");

        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        if (!salva.Ok)
            return Resultado<bool>.Falha(salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Mensagem ?? "Não consegui tirar a etiqueta.");

        log.LogInformation("Tirei {Etiqueta} de {Nota}.", etiqueta, caminho);
        return Resultado<bool>.Sucesso(true);
    }

    /// <summary>
    /// AS ETIQUETAS QUE COSTUMAM VIR JUNTO DAS QUE ESTA NOTA JÁ TEM.
    ///
    /// É A RELAÇÃO ENTRE ETIQUETAS APARECENDO ONDE ELA SERVE. A coocorrência já existia — é ela que
    /// desenha o mapa em /etiquetas/mapa —, mas um mapa responde "o que anda junto no meu vault", que é
    /// uma pergunta de fim de semana. A pergunta do dia a dia é outra: "acabei de marcar #tributário
    /// nesta nota; o que eu costumo marcar junto e esqueci agora?". A resposta é a MESMA conta, servida
    /// no lugar onde ela vira ação em vez de contemplação.
    ///
    /// COMO SE LÊ: "#tributário e #decorar dividem nota 8 vezes no seu vault". Ninguém escreveu essa
    /// ligação — ela existe só no conjunto, e é por isso que ela surpreende quem a vê.
    ///
    /// NOTA SEM ETIQUETA NENHUMA recebe as MAIS USADAS do vault, e não uma lista vazia. É exatamente
    /// quem mais precisa de sugestão: uma nota nova não tem com o que coocorrer, e uma caixa vazia ali
    /// ensinaria que este bloco não serve para nada.
    /// </summary>
    /// <remarks>
    /// A REGRA MORA NO DOMÍNIO (<see cref="SugestaoDeEtiquetas"/>): o que conta como parente, como se
    /// ordena e o que fazer quando não há parente nenhum são decisões, e decisão sem teste muda sozinha.
    /// Aqui sobra o que é de aplicação: buscar as etiquetas de cada nota e montar o grafo.
    /// </remarks>
    public async Task<IReadOnlyList<EtiquetaSugerida>> SugerirEtiquetasAsync(
        IReadOnlyCollection<Etiqueta> jaTem, int limite = 6, CancellationToken ct = default)
    {
        var porNota = await indice.EtiquetasPorNotaAsync(ct);
        var (nos, pares) = GrafoDeEtiquetas.Montar(porNota);
        return SugestaoDeEtiquetas.Para(nos, pares, jaTem, limite);
    }

    /// <summary>
    /// O MAPA DAS MATÉRIAS — o topo do drill do grafo. Ver <see cref="GrafoDeMaterias"/> para as
    /// decisões; aqui é só a costura: montar o grafo de notas, agregar, posicionar.
    ///
    /// MONTA O GRAFO INTEIRO PARA AGREGAR, e isso é deliberado, não desperdício: a ponte entre matérias
    /// só existe olhando as ligações nota a nota, e é a MESMA montagem do GrafoAsync — mesmos recortes
    /// (templates fora, quebradas fora), mesma conta. Duas fontes para o mesmo número é uma delas
    /// ficando para trás.
    /// </summary>
    public async Task<MapaDeMaterias> MapaDeMateriasAsync(
        double largura = 800, double altura = 600, CancellationToken ct = default)
    {
        var caminhos = (await indice.TodosOsCaminhosAsync(ct)).Where(EhConteudoDeEstudo).ToList();
        if (caminhos.Count == 0) return MapaDeMaterias.Vazio;

        var ligacoes = new List<LigacaoResolvida>();
        foreach (var c in caminhos) ligacoes.AddRange(await indice.LigacoesDeAsync(c, ct));

        var (nos, pontes) = GrafoDeMaterias.Montar(GrafoDoVault.Montar(caminhos, ligacoes));
        if (nos.Count == 0) return MapaDeMaterias.Vazio;

        // A SEMENTE É O RÓTULO, não o Nome: "Sem matéria" tem Nome vazio, e duas sementes vazias
        // colapsariam no mesmo hash. O grau que vira raio é QUANTAS NOTAS — o tamanho do nó diz
        // "quanto existe ali dentro", que é a única grandeza que este nível tem.
        var posicoes = LayoutDeForca.Posicionar(
            [.. nos.Select(n => n.Materia.Rotulo)],
            [.. nos.Select(n => n.Notas)],
            [.. nos.Select(_ => -1)],
            [.. pontes.Select(p => new ArestaDoGrafo(p.De, p.Para, p.Peso))],
            largura, altura);

        return new MapaDeMaterias(nos, posicoes, pontes, largura, altura);
    }

    /// <summary>
    /// Os pares de notas de uma ponte do mapa das matérias. A regra é do domínio
    /// (<see cref="GrafoDeMaterias.ParesDaPonte"/>); aqui só se monta o mesmo grafo de sempre.
    /// </summary>
    public async Task<IReadOnlyList<ParDaPonte>> PonteAsync(
        Materia a, Materia b, CancellationToken ct = default)
    {
        var caminhos = (await indice.TodosOsCaminhosAsync(ct)).Where(EhConteudoDeEstudo).ToList();
        if (caminhos.Count == 0) return [];

        var ligacoes = new List<LigacaoResolvida>();
        var titulos = new Dictionary<CaminhoNota, string>();
        foreach (var c in caminhos)
        {
            ligacoes.AddRange(await indice.LigacoesDeAsync(c, ct));
            if (await indice.ObterAsync(c, ct) is { } n) titulos[c] = n.Titulo;
        }

        return GrafoDeMaterias.ParesDaPonte(GrafoDoVault.Montar(caminhos, ligacoes, titulos), a, b);
    }

    // —— LIGAR DUAS NOTAS ——————————————————————————————————————————————————————————————
    /// <summary>
    /// Escreve em <paramref name="dentroDe"/> uma ligação para <paramref name="apontarPara"/>.
    ///
    /// UM MÉTODO PARA AS DUAS DIREÇÕES, e é isso que o torna simples. "Esta nota aponta para X" e
    /// "faça X apontar para cá" parecem dois recursos e são o mesmo: escrever um wikilink DENTRO de uma
    /// nota. O que muda é qual das duas é a de dentro. Dois métodos aqui seriam duas cópias das mesmas
    /// regras para manter iguais.
    ///
    /// ESCREVER NUMA NOTA QUE NÃO ESTÁ ABERTA é legítimo e não pode ser silencioso: num produto cujo
    /// lema é "o vault é seu", editar arquivo por baixo do pano é grave. Por isso o resultado diz em
    /// QUAL nota escreveu e O QUE escreveu — é a tela que conta, mas quem sabe é aqui.
    ///
    /// PASSA PELO ServicoDeNotas.SalvarAsync, e não grava direto: é ele quem arquiva no histórico e
    /// reindexa. Gravar por fora faria a ligação existir no arquivo e não no grafo — que é justamente
    /// a tela que este recurso existe para alimentar.
    /// </summary>
    public async Task<Resultado<LigacaoEscrita>> LigarAsync(
        CaminhoNota dentroDe, CaminhoNota apontarPara, CancellationToken ct = default)
    {
        if (dentroDe == apontarPara)
            return Resultado<LigacaoEscrita>.Invalida("Uma nota não aponta para ela mesma.");

        var nota = await repositorio.LerAsync(dentroDe, ct);
        if (nota is null) return Resultado<LigacaoEscrita>.NaoEncontrada($"A nota \"{dentroDe}\"");

        // O TEXTO MAIS CURTO QUE AINDA RESOLVE: só o nome quando ele é único no vault, o caminho quando
        // há homônimas. Escrever sempre o caminho funcionaria e deixaria a nota feia de ler no Obsidian,
        // que é onde ela vai ser lida.
        var todas = await indice.TodosOsCaminhosAsync(ct);
        var texto = EscritaDeLigacao.MaisCurta(apontarPara, todas);

        var (conteudo, oQueHouve) = SecaoDeRelacionadas.Ligar(
            nota.Conteudo, texto, nota.Analise.Ligacoes.Select(l => l.Alvo));

        if (oQueHouve == SecaoDeRelacionadas.Resultado.JaHavia)
            return Resultado<LigacaoEscrita>.Sucesso(new LigacaoEscrita(dentroDe, texto, JaHavia: true));

        var salva = await notas.SalvarAsync(dentroDe, conteudo, nota.Impressao, autor: null, ct);
        if (!salva.Ok)
            return Resultado<LigacaoEscrita>.Falha(salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Mensagem ?? "Não consegui escrever a ligação.");

        log.LogInformation("Liguei {Origem} → {Alvo}.", dentroDe, apontarPara);
        return Resultado<LigacaoEscrita>.Sucesso(new LigacaoEscrita(dentroDe, texto, JaHavia: false));
    }

    // —— MATÉRIA DE REFERÊNCIA ————————————————————————————————————————————————————————
    /// <summary>
    /// Liga e desliga a cobrança de cartões numa matéria, escrevendo no frontmatter da nota-índice dela.
    ///
    /// POR QUE ISTO EXISTE: o painel cobra cartões de toda matéria que tem nota e não tem cartão, e essa
    /// opinião está certa para material de estudo. Para uma pasta de trabalho — anotação de reunião,
    /// procedimento, referência que se consulta — ela está errada todo dia, e a matéria com mais notas é
    /// justamente a que ganha a sugestão principal. Ver EditorDeFrontmatter.CampoReferencia.
    ///
    /// CRIA A NOTA-ÍNDICE SE NÃO HOUVER. A marca precisa de um arquivo onde morar, e uma matéria criada
    /// direto no disco (por quem arrastou uma pasta para o vault) não tem esse arquivo. Criar o mapa da
    /// matéria é bom por si só — é a mesma nota que a criação pela tela já geraria.
    /// </summary>
    public async Task<Resultado<bool>> AlternarReferenciaAsync(Materia materia, CancellationToken ct = default)
    {
        if (!materia.Existe)
            return Resultado<bool>.Invalida("Nota sem matéria não pode ser marcada como referência.");

        if (!CaminhoNota.TentarCriar($"{materia.Nome}/{materia.Nome}{CaminhoNota.Extensao}",
                out var caminho, out var erro) || caminho is null)
            return Resultado<bool>.Invalida(erro ?? "Nome de matéria inválido.");

        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null)
        {
            var criada = await notas.CriarAsync(caminho, ConteudoDaNotaIndice(materia), ct);
            if (!criada.Ok)
                return Resultado<bool>.Falha(criada.Motivo ?? MotivoDaFalha.Invalida,
                    criada.Mensagem ?? "Não consegui criar o mapa da matéria.");
            nota = criada.Valor;
        }

        if (nota is null) return Resultado<bool>.NaoEncontrada($"O mapa da matéria \"{materia.Rotulo}\"");

        var novoEstado = !EditorDeFrontmatter.EhReferencia(nota.Analise);
        var conteudo = EditorDeFrontmatter.DefinirReferencia(nota.Conteudo, novoEstado);

        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        if (!salva.Ok)
            return Resultado<bool>.Falha(salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Mensagem ?? "Não consegui marcar.");

        log.LogInformation("Matéria {Materia} agora é referência: {Estado}.", materia.Rotulo, novoEstado);
        return Resultado<bool>.Sucesso(novoEstado);
    }

    /// <summary>As matérias marcadas como material de consulta. Vazio é o normal.</summary>
    public async Task<IReadOnlySet<Materia>> ReferenciasAsync(CancellationToken ct = default)
    {
        var marcadas = new HashSet<Materia>();
        foreach (var caminho in await indice.TodosOsCaminhosAsync(ct))
        {
            if (!EhNotaIndiceDaMateria(caminho)) continue;
            var nota = await repositorio.LerAsync(caminho, ct);
            if (nota is not null && EditorDeFrontmatter.EhReferencia(nota.Analise))
                marcadas.Add(Materia.De(caminho));
        }
        return marcadas;
    }

    /// <summary>
    /// O mapa de conteúdo que nasce com a matéria. Markdown comum — abre igual no Obsidian.
    ///
    /// Público porque é função pura do nome e vale testar sozinha: foi aqui que a citação de
    /// "[[wikilinks]]" no texto de ajuda virou uma ligação quebrada de verdade em toda matéria nova.
    /// </summary>
    public static string ConteudoDaNotaIndice(Materia materia) =>
        $"""
        ---
        tags: [materia]
        ---
        # {materia.Nome}

        Mapa desta matéria. Escreva aqui o plano de estudo e ligue os assuntos com `[[wikilinks]]` —
        o que sair daqui vira aresta no grafo, e o que você citar e ainda não escreveu aparece
        em "ainda por escrever".

        ## Assuntos

        ## A revisar

        """;

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
    public async Task<VisaoDoGrafo> GrafoAsync(
        CaminhoNota? centro = null, int saltos = 2, double largura = 900, double altura = 620,
        Materia? materia = null, CancellationToken ct = default)
    {
        // Mesmo recorte da lista de matérias: template é ferramenta, não conhecimento, e um nó "{{titulo}}"
        // no grafo é ruído que nunca vai se ligar a nada.
        var caminhos = (await indice.TodosOsCaminhosAsync(ct)).Where(EhConteudoDeEstudo).ToList();
        if (caminhos.Count == 0) return VisaoDoGrafo.Vazia;

        var ligacoes = new List<LigacaoResolvida>();
        var titulos = new Dictionary<CaminhoNota, string>();
        foreach (var c in caminhos)
        {
            ligacoes.AddRange(await indice.LigacoesDeAsync(c, ct));
            if (await indice.ObterAsync(c, ct) is { } n) titulos[c] = n.Titulo;
        }

        var grafo = GrafoDoVault.Montar(caminhos, ligacoes, titulos);
        if (centro is not null) grafo = grafo.Vizinhanca(centro, saltos);

        // A legenda sai daqui, ANTES do recorte por matéria: é ela que oferece a troca de matéria, e
        // depois do recorte só sobraria a que já está escolhida.
        var materias = grafo.Nos
            .GroupBy(n => n.Materia)
            .Select(g => new MateriaContada(g.Key, g.Count()))
            .OrderByDescending(m => m.Notas)
            .ThenBy(m => m.Materia.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var paraFora = 0;
        if (materia is not null)
        {
            // Matéria pedida na URL que não existe neste recorte: devolve o grafo inteiro em vez de uma
            // tela vazia. Um endereço antigo, de uma pasta já renomeada, tem de degradar para algo útil.
            if (materias.Any(m => m.Materia == materia))
            {
                var recorte = grafo.DaMateria(materia);
                grafo = recorte.Grafo;
                paraFora = recorte.LigacoesParaFora;
            }
            else
            {
                materia = null;
            }
        }

        return new VisaoDoGrafo(LayoutDeForca.Calcular(grafo, largura, altura), materias, materia, paraFora);
    }

    /// <summary>
    /// Quantas ligações apontam para esta nota. É o que a tela mostra ANTES de confirmar um excluir: a
    /// pessoa merece saber quantos caminhos vai cortar.
    ///
    /// Excluir NÃO apaga esses links, e isso é decisão e não omissão — no Obsidian, link para nota
    /// inexistente é RECURSO: você escreve o link antes de escrever a nota, e ele fica como lembrete do
    /// que falta. Apagá-los destruiria texto escrito em OUTRAS notas para registrar uma decisão sobre
    /// ESTA. O que o produto deve é avisar antes, e é o que este número serve para fazer.
    /// </summary>
    public async Task<int> QuantasApontamParaAsync(CaminhoNota caminho, CancellationToken ct = default) =>
        (await indice.BacklinksAsync(caminho, ct)).Count;

    // —— ABRIR RÁPIDO ————————————————————————————————————————————————————————————————
    /// <summary>
    /// Os candidatos do abridor rápido, já ordenados. Com o termo VAZIO devolve as recentes: quem abre
    /// o buscador sem digitar quase sempre quer voltar para onde estava, e uma lista alfabética ali
    /// seria a informação menos útil que existe.
    /// </summary>
    public async Task<IReadOnlyList<NotaIndexada>> ParaAbrirRapidoAsync(
        string? termo, int limite = 12, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return await indice.RecentesAsync(limite, ct);

        // A busca por NOME roda sobre o caminho inteiro — "dirtrib/lic" tem de funcionar, porque é
        // assim que se lembra de uma nota: pela matéria mais o nome.
        var todos = await indice.TodosOsCaminhosAsync(ct);
        var porCaminho = todos.ToDictionary(c => c.Valor, c => c, StringComparer.Ordinal);

        var ordenados = BuscaPorNome.Ordenar(termo, porCaminho.Keys)
            .Take(limite)
            .Select(a => porCaminho[a.Texto])
            .ToList();

        // O índice tem o título e as etiquetas; o caminho sozinho não. Buscar um a um é barato porque
        // são no máximo `limite` itens — e é o que evita a tela mostrar nome de arquivo cru.
        var notas = new List<NotaIndexada>(ordenados.Count);
        foreach (var caminho in ordenados)
        {
            var n = await indice.ObterAsync(caminho, ct);
            if (n is not null) notas.Add(n);
        }
        return notas;
    }

    // —— PAINEL DE ETIQUETAS ————————————————————————————————————————————————————————
    /// <summary>
    /// A árvore de etiquetas do vault, com as contagens certas. Ver <see cref="ArvoreDeEtiquetas"/> para
    /// por que a contagem não é a soma dos filhos.
    /// </summary>
    /// <summary>
    /// Todas as notas do vault, em ordem de caminho — para uma tela que precisa oferecer "em qual nota".
    /// Ordenado aqui e não na tela: duas telas com a mesma lista em ordens diferentes é o tipo de coisa
    /// que ninguém reporta e todo mundo estranha.
    /// </summary>
    public async Task<IReadOnlyList<CaminhoNota>> TodosOsCaminhosAsync(CancellationToken ct = default) =>
        (await indice.TodosOsCaminhosAsync(ct))
            .OrderBy(c => c.Valor, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public async Task<IReadOnlyList<NoDeEtiqueta>> PainelDeEtiquetasAsync(CancellationToken ct = default) =>
        ArvoreDeEtiquetas.Montar(await indice.EtiquetasPorNotaAsync(ct));

    // —— PARECIDAS AINDA NÃO LIGADAS ——————————————————————————————————————————————————
    /// <summary>
    /// As notas que falam do mesmo assunto que esta e ainda NÃO estão ligadas a ela — em nenhuma direção.
    ///
    /// É a peça que faltava para a conexão SURGIR em vez de depender de a pessoa lembrar: as menções não
    /// ligadas pegam citação literal do nome, mas duas notas sobre o mesmo tema com palavras diferentes
    /// passavam batidas uma pela outra para sempre. O parentesco aqui vem de dois sinais que já estão no
    /// índice: ETIQUETAS EM COMUM (o que as duas SÃO) e ALVOS EM COMUM (as duas citarem as mesmas notas —
    /// quem cita [[CTN]] e [[Decadência]] anda no mesmo terreno).
    ///
    /// SEM VARRER O VAULT: cada sinal é uma consulta pontual ao índice (notas por etiqueta, backlinks de
    /// um alvo), então o custo acompanha o tamanho da NOTA, não o do vault — a mesma regra das menções.
    /// </summary>
    public async Task<IReadOnlyList<NotaParecida>> ParecidasAsync(
        CaminhoNota caminho, int limite = 5, CancellationToken ct = default)
    {
        var alvo = await indice.ObterAsync(caminho, ct);
        if (alvo is null) return [];

        // Quem já está ligado (em qualquer direção) está fora: a lista existe para o que FALTA ligar.
        var jaLigadas = new HashSet<CaminhoNota>((await indice.LigacoesDeAsync(caminho, ct))
            .Where(l => l.Destino is not null).Select(l => l.Destino!));
        jaLigadas.UnionWith((await indice.BacklinksAsync(caminho, ct)).Select(b => b.Origem));
        jaLigadas.Add(caminho);

        var pontos = new Dictionary<CaminhoNota, int>();
        var motivos = new Dictionary<CaminhoNota, List<string>>();

        void Somar(CaminhoNota quem, int quanto, string motivo)
        {
            if (jaLigadas.Contains(quem)) return;
            pontos[quem] = pontos.GetValueOrDefault(quem) + quanto;
            (motivos.TryGetValue(quem, out var m) ? m : motivos[quem] = []).Add(motivo);
        }

        // ETIQUETA EM COMUM pesa mais que alvo em comum: etiqueta é uma afirmação da pessoa sobre o que a
        // nota É; citar a mesma nota pode ser coincidência de passagem.
        foreach (var etiqueta in alvo.Etiquetas.Take(8))
        {
            foreach (var acerto in await indice.BuscarAsync(
                         new ConsultaDeBusca { Etiqueta = etiqueta, Limite = 60 }, ct))
                Somar(acerto.Nota.Caminho, 2, $"também tem {etiqueta}");
        }

        var saidas = (await indice.LigacoesDeAsync(caminho, ct))
            .Where(l => l.Destino is not null).Select(l => l.Destino!).Distinct().Take(8);
        foreach (var destino in saidas)
        {
            foreach (var b in await indice.BacklinksAsync(destino, ct))
                Somar(b.Origem, 1, $"também cita {destino.Nome}");
        }

        return [.. pontos
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key.Valor, StringComparer.CurrentCultureIgnoreCase)
            .Take(limite)
            // Um motivo só, o mais forte: a lista explica POR QUE sugere, e três motivos empilhados numa
            // linha de painel viram ruído — quem quiser o resto abre a nota.
            .Select(p => new NotaParecida(p.Key, motivos[p.Key].First(), p.Value))];
    }

    // —— BACKLINKS COM O TRECHO ——————————————————————————————————————————————————————
    /// <summary>
    /// Os backlinks de uma nota COM a frase em volta de cada citação — a metade que faltava para o
    /// painel dizer COMO as outras notas apontam para cá, e não só QUE apontam. Ver TrechoDaLigacao.
    /// Lê cada nota de origem uma vez (agrupado), fora do caminho de abertura — quem chama é o painel
    /// lateral, depois que a nota já está na tela.
    /// </summary>
    public async Task<IReadOnlyList<BacklinkComTrecho>> BacklinksComTrechoAsync(
        CaminhoNota caminho, int trechosPorNota = 3, CancellationToken ct = default)
    {
        var backlinks = await indice.BacklinksAsync(caminho, ct);
        var resultado = new List<BacklinkComTrecho>();

        foreach (var grupo in backlinks.Where(b => !b.EhInterna).GroupBy(b => b.Origem))
        {
            var nota = await repositorio.LerAsync(grupo.Key, ct);
            if (nota is null) continue;

            // Até N trechos por nota de origem: quem cita cinco vezes tem cinco frases, mas o painel é
            // um resumo — as três primeiras dizem o suficiente para decidir se vale abrir.
            foreach (var l in grupo.OrderBy(l => l.Posicao).Take(trechosPorNota))
                resultado.Add(new BacklinkComTrecho(grupo.Key,
                    TrechoDaLigacao.EmVolta(nota.Conteudo, l.Posicao, l.Alvo.Length + 4)));
        }
        return resultado;
    }

    // —— MENÇÕES NÃO LIGADAS ————————————————————————————————————————————————————————
    /// <summary>
    /// As notas que citam esta pelo nome sem ligar para ela.
    ///
    /// A LISTA DE CANDIDATAS VEM DA BUSCA DE TEXTO, e não de uma varredura do vault inteiro. A diferença
    /// aparece no vault de dois anos: ler todas as notas do disco a cada abertura de nota transformaria
    /// um painel lateral no gargalo do produto. A busca já sabe quem contém a palavra; ao domínio só cabe
    /// decidir, nessas poucas, se a ocorrência é uma menção de verdade.
    ///
    /// O PREÇO DESSA ESCOLHA, dito às claras: o que a busca de texto não encontra, este painel não mostra.
    /// Na prática isso significa depender de como o índice normaliza acento e radical — e é por isso que
    /// se procura por cada nome e apelido separadamente, em vez de confiar numa consulta só.
    /// </summary>
    public async Task<IReadOnlyList<MencaoEmNota>> MencoesNaoLigadasAsync(
        CaminhoNota caminho, int limiteDeNotas = 20, CancellationToken ct = default)
    {
        var alvo = await indice.ObterAsync(caminho, ct);
        if (alvo is null) return [];

        var nomes = new List<string> { caminho.Nome };
        nomes.AddRange(alvo.Apelidos);

        // Candidatas: quem contém alguma das palavras. Sem a própria nota, que sempre cita o próprio nome.
        var candidatas = new Dictionary<CaminhoNota, NotaIndexada>();
        foreach (var nome in nomes.Where(n => n.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var acerto in await indice.BuscarAsync(
                         new ConsultaDeBusca { Texto = nome, Limite = limiteDeNotas }, ct))
            {
                if (acerto.Nota.Caminho == caminho) continue;

                // XARÁ NÃO É MENÇÃO. Duas notas "Prescrição" em matérias diferentes existem de verdade num
                // vault de concurso, e o título de uma delas — "# Prescrição", na primeira linha — casaria
                // com o nome da outra. Sugerir ligar ali é oferecer trocar o próprio título por um link
                // para outra nota, que é o contrário do que a pessoa quer.
                if (nomes.Any(n => string.Equals(n, acerto.Nota.Caminho.Nome, StringComparison.OrdinalIgnoreCase)))
                    continue;

                candidatas.TryAdd(acerto.Nota.Caminho, acerto.Nota);
            }
        }

        var encontradas = new List<MencaoEmNota>();
        foreach (var (candidata, indexada) in candidatas.Take(limiteDeNotas))
        {
            ct.ThrowIfCancellationRequested();

            var nota = await repositorio.LerAsync(candidata, ct);
            if (nota is null) continue;   // o disco mudou desde a última reconciliação

            foreach (var mencao in MencoesNaoLigadas.Encontrar(nota.Conteudo, nomes))
                encontradas.Add(new MencaoEmNota(candidata, indexada.Titulo, mencao));
        }

        return encontradas;
    }

    /// <summary>
    /// Transforma uma menção em ligação, gravando a nota que a contém.
    ///
    /// GRAVA PELO <see cref="ServicoDeNotas"/>, e não direto no repositório: é ali que mora a invariante
    /// de que toda gravação reindexa e cria revisão. Escrever por fora deixaria o índice mentindo até o
    /// vigia passar — e a menção continuaria na lista, como se o clique não tivesse feito nada.
    /// </summary>
    public async Task<Resultado<int>> LigarMencaoAsync(
        CaminhoNota onde, Mencao mencao, CaminhoNota alvo, CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(onde, ct);
        if (nota is null) return Resultado<int>.NaoEncontrada($"A nota \"{onde.Valor}\"");

        var todos = await indice.TodosOsCaminhosAsync(ct);
        var novo = MencoesNaoLigadas.Ligar(nota.Conteudo, mencao, EscritaDeLigacao.MaisCurta(alvo, todos));

        // Null aqui significa que o texto mudou embaixo entre listar e clicar. Falhar em voz alta é o
        // contrário de gravar por cima da posição antiga e estragar uma frase qualquer em silêncio.
        if (novo is null)
            return Resultado<int>.Falha(MotivoDaFalha.Conflito,
                "Esta nota mudou desde que a lista foi montada. Abra o painel de novo para ver onde a menção está agora.");

        var gravada = await notas.SalvarAsync(onde, novo, nota.Impressao, ct: ct);
        if (!gravada.Ok)
            return Resultado<int>.Falha(
                gravada.Motivo ?? MotivoDaFalha.Invalida, gravada.Mensagem ?? "Não consegui gravar a nota.");

        log.LogInformation("Menção ligada em {Onde} para {Alvo}.", onde.Valor, alvo.Valor);
        return Resultado<int>.Sucesso(1);
    }

    // —— COMPLETAR [[ ]] ————————————————————————————————————————————————————————————
    /// <summary>
    /// Os candidatos do autocompletar de <c>[[</c>, já ordenados e já com o texto pronto para inserir.
    ///
    /// POR QUE ESTE MÉTODO EXISTE, em vez de a tela reaproveitar <see cref="ParaAbrirRapidoAsync"/>:
    /// abrir e citar são gestos diferentes. Quem abre precisa do caminho para navegar; quem cita precisa
    /// do TEXTO QUE VAI FICAR ESCRITO na nota — e decidir se ele sai curto ou com a pasta exige olhar o
    /// vault inteiro atrás de homônimas. Deixar essa conta para o JavaScript colocaria uma regra do
    /// domínio dentro de um arquivo .js, onde ninguém a testaria e ela divergiria do renomear.
    ///
    /// Com termo VAZIO devolve as recentes — quem acabou de digitar "[[" quase sempre quer citar algo em
    /// que estava mexendo, e uma lista alfabética ali seria a informação menos útil possível.
    /// </summary>
    public async Task<IReadOnlyList<SugestaoDeLigacao>> ParaCompletarLigacaoAsync(
        string? termo, int limite = 8, CancellationToken ct = default)
    {
        var todos = await indice.TodosOsCaminhosAsync(ct);

        IEnumerable<CaminhoNota> escolhidos;
        if (string.IsNullOrWhiteSpace(termo))
        {
            escolhidos = (await indice.RecentesAsync(limite, ct)).Select(n => n.Caminho);
        }
        else
        {
            var porCaminho = todos.ToDictionary(c => c.Valor, c => c, StringComparer.Ordinal);
            // O CRIVO CONTÍGUO ANTES DA ORDENAÇÃO. Aceitar uma sugestão daqui ESCREVE um link no
            // arquivo — precisão vale mais que alcance. Por subsequência pura, "deca" casava com
            // "o que cai DE Contabilidade" e o Enter linkava a nota errada. Ver BuscaPorNome.ContemTrecho;
            // o abridor rápido continua com a busca solta, porque lá errar custa só um Esc.
            escolhidos = BuscaPorNome.Ordenar(termo,
                    porCaminho.Keys.Where(c => BuscaPorNome.ContemTrecho(termo, c)))
                .Take(limite)
                .Select(a => porCaminho[a.Texto]);
        }

        return escolhidos
            .Select(c => new SugestaoDeLigacao(
                c.Nome, c.Pasta, EscritaDeLigacao.MaisCurta(c, todos)))
            .ToList();
    }

    // —— COMPLETAR # ————————————————————————————————————————————————————————————————
    /// <summary>
    /// As etiquetas candidatas para o que já foi digitado depois do "#".
    ///
    /// POR QUE ELE OFERECE SÓ O QUE JÁ EXISTE: a lista não está aqui para poupar teclado, e sim para
    /// impedir que o vault se despedace em sinônimos. Ninguém lembra, três semanas depois, se marcou
    /// #pegadinha ou #pegadinhas; sem a lista, marca-se a variação nova e cada uma passa a ter um pedaço
    /// do assunto — sem erro nenhum na tela. Ver BuscaDeEtiquetas.
    ///
    /// Etiqueta NOVA continua sendo só digitar e não escolher nada da lista. É de propósito que não haja
    /// um item "criar #assim": o gesto de criar tem de ser o gesto normal de escrever, e destacá-lo
    /// convidaria a criar justamente quando a lista está mostrando que já existe uma parecida.
    ///
    /// AS ANCESTRAIS VÊM JUNTO porque é assim que o índice as guarda: quem tem "#direito/tributário"
    /// também aparece em "#direito", e oferecer os dois níveis é o que permite escolher a largura da
    /// marca na hora de marcar.
    /// </summary>
    public async Task<IReadOnlyList<SugestaoDeEtiqueta>> ParaCompletarEtiquetaAsync(
        string? termo, int limite = 8, CancellationToken ct = default)
    {
        var contadas = await indice.EtiquetasAsync(ct);

        return BuscaDeEtiquetas
            .Ordenar(termo, contadas.Select(c => (c.Etiqueta, c.Notas)))
            .Take(limite)
            .Select(a => new SugestaoDeEtiqueta(a.Etiqueta.Valor, Contar(a.Notas)))
            .ToList();
    }

    /// <summary>
    /// "40 notas" / "1 nota" — o número existe para dizer QUAL É A ETIQUETA DE VERDADE quando duas se
    /// parecem. É a única informação que separa a que você usa da que escapou uma vez.
    /// </summary>
    private static string Contar(int notas) => notas == 1 ? "1 nota" : $"{notas} notas";
}

/// <summary>
/// Um candidato do autocompletar de <c>[[</c>.
///
/// <see cref="Insercao"/> é separado de <see cref="Nome"/> porque nem sempre são iguais: quando existe
/// outra nota com o mesmo nome, o que se mostra continua sendo o nome — é por ele que a pessoa reconhece
/// a nota — mas o que se escreve tem de ser o caminho, ou o link fica ambíguo.
/// </summary>
public sealed record SugestaoDeLigacao(string Nome, string Pasta, string Insercao);

/// <summary>
/// Um candidato do autocompletar de <c>#</c>.
///
/// <see cref="Uso"/> é texto pronto ("40 notas") e não um número: quem desenha a lista não deveria ter de
/// saber decidir entre "1 nota" e "1 notas", e essa é uma decisão de idioma, não de tela.
/// </summary>
public sealed record SugestaoDeEtiqueta(string Valor, string Uso);

/// <summary>
/// A frase que a tela mostra para uma etiqueta sugerida. Fica aqui, e não no domínio, porque é texto de
/// interface: o domínio decide o PESO e o motivo; como isso vira português é da camada de fora.
/// </summary>
public static class ComoExplicar
{
    public static string A(EtiquetaSugerida s) => s.PorCoocorrencia
        ? s.Peso == 1 ? "divide 1 nota com as desta" : $"divide {s.Peso} notas com as desta"
        : s.Peso == 1 ? "1 nota" : $"{s.Peso} notas";
}

/// <summary>Uma menção não ligada, com a nota em que ela está.</summary>
public sealed record MencaoEmNota(CaminhoNota Onde, string Titulo, Mencao Mencao);

/// <summary>Um backlink com a frase em volta da citação — a metade que faz o painel dizer COMO apontam.</summary>
public sealed record BacklinkComTrecho(CaminhoNota Origem, string Trecho);

/// <summary>
/// Uma nota parecida com a aberta e ainda não ligada a ela. O motivo é UM, o mais forte — a lista
/// existe para explicar por que sugere, não para provar o parentesco por exaustão.
/// </summary>
public sealed record NotaParecida(CaminhoNota Caminho, string Motivo, int Pontos);
