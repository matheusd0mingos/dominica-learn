using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

/// <summary>A nota aberta, com tudo que a tela precisa mostrar de uma vez.</summary>
public sealed record NotaAberta(
    Nota Nota,
    IReadOnlyList<LigacaoResolvida> Backlinks,
    IReadOnlyList<LigacaoResolvida> Saidas);

/// <summary>
/// Os comandos do dia a dia sobre uma nota: abrir, salvar, criar, renomear, apagar.
///
/// Ficam juntos porque compartilham a MESMA invariante — toda escrita no disco tem de ser seguida de
/// reindexação, e toda leitura tem de saber que o disco pode ter mudado por fora. Separá-los em cinco
/// classes espalharia essa regra por cinco lugares, e é exatamente esse tipo de regra que se esquece de
/// aplicar no sexto.
/// </summary>
public sealed class ServicoDeNotas(
    IRepositorioDeNotas repositorio,
    IIndiceDoVault indice,
    IHistoricoDeNotas historico,
    ReconciliarVault reconciliacao,
    IRelogio relogio,
    ILogger<ServicoDeNotas> log)
{
    public async Task<Resultado<NotaAberta>> AbrirAsync(CaminhoNota caminho, CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado<NotaAberta>.NaoEncontrada($"A nota \"{caminho}\"");

        var backlinks = await indice.BacklinksAsync(caminho, ct);
        var saidas = await indice.LigacoesDeAsync(caminho, ct);
        return Resultado<NotaAberta>.Sucesso(new NotaAberta(nota, backlinks, saidas));
    }

    /// <summary>
    /// Salva o conteúdo.
    ///
    /// <paramref name="impressaoEsperada"/> é a impressão que o cliente tinha quando começou a editar.
    /// Se o disco estiver diferente disso, ALGUÉM MAIS ESCREVEU — outra aba, o Obsidian no notebook, um
    /// sincronizador de nuvem — e sobrescrever seria apagar o trabalho dessa pessoa em silêncio. O caso
    /// de uso recusa e devolve <see cref="MotivoDaFalha.Conflito"/> para a interface resolver com quem
    /// está lá. Passar null desliga a verificação: é a saída explícita para "eu sei, sobrescreve".
    /// </summary>
    public async Task<Resultado<Nota>> SalvarAsync(
        CaminhoNota caminho, string conteudo, ImpressaoDigital? impressaoEsperada,
        string? autor = null, CancellationToken ct = default)
    {
        var atual = await repositorio.LerAsync(caminho, ct);

        if (impressaoEsperada is { } esperada && atual is not null && !atual.Impressao.Equals(esperada))
        {
            log.LogWarning("Conflito ao salvar {Caminho}: o disco mudou por fora.", caminho);
            return Resultado<Nota>.Falha(MotivoDaFalha.Conflito,
                "Esta nota mudou fora do editor desde que você a abriu. Recarregue para não perder o que foi escrito do outro lado.");
        }

        // Nada mudou: não escreve. O autosave dispara a cada poucos segundos e gravar texto idêntico
        // sujaria o histórico, mexeria na data de modificação e acordaria o vigia de arquivos à toa.
        if (atual is not null && atual.Impressao.Equals(ImpressaoDigital.De(conteudo)))
            return Resultado<Nota>.Sucesso(atual);

        // arquiva o que estava lá ANTES de sobrescrever — é a única chance de guardar aquele texto
        if (atual is not null) await historico.ArquivarAsync(caminho, atual.Conteudo, autor, ct);

        var gravada = await repositorio.GravarAsync(caminho, conteudo, ct);
        await ReindexarUmaAsync(gravada.Caminho, ct);
        return Resultado<Nota>.Sucesso(gravada);
    }

    public async Task<Resultado<Nota>> CriarAsync(CaminhoNota caminho, string conteudoInicial = "", CancellationToken ct = default)
    {
        if (await repositorio.ExisteAsync(caminho, ct)) return Resultado<Nota>.JaExiste($"A nota \"{caminho}\"");

        var nota = await repositorio.GravarAsync(caminho, conteudoInicial, ct);
        await ReindexarUmaAsync(nota.Caminho, ct);

        // A NOTA QUE ACABOU DE NASCER PODE SER O DESTINO QUE FALTAVA. Escrever "[[Prescrição]]" antes de
        // criar a nota é o fluxo normal daqui — a lista "Ainda por escrever" é um convite a fazer isso, e
        // clicar num item cai exatamente neste método. Sem esta linha, a ligação continuava quebrada no
        // índice para sempre. Ver ReconciliarVault.ConsertarLigacoesQuebradasAsync.
        await ConsertarQuemEsperavaAsync(ct);

        log.LogInformation("Nota criada: {Caminho}", caminho);
        return Resultado<Nota>.Sucesso(nota);
    }

    /// <summary>
    /// Renomeia/move a nota E REESCREVE OS WIKILINKS que apontavam para ela.
    ///
    /// Sem a reescrita, renomear quebraria todas as ligações de entrada de uma vez — e num vault de
    /// estudo, onde a rede de ligações É o conhecimento, isso é destruir o produto para arrumar o nome
    /// de um arquivo. É o preço de identificar a nota pelo caminho (ver <see cref="CaminhoNota"/>), e
    /// pagá-lo aqui é o que torna aquela escolha sustentável.
    /// </summary>
    public async Task<Resultado<Nota>> RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
    {
        if (de.Equals(para)) return Resultado<Nota>.Invalida("O novo caminho é igual ao atual.");
        if (!await repositorio.ExisteAsync(de, ct)) return Resultado<Nota>.NaoEncontrada($"A nota \"{de}\"");
        if (await repositorio.ExisteAsync(para, ct)) return Resultado<Nota>.JaExiste($"A nota \"{para}\"");

        // quem aponta para cá tem de ser corrigido — colhido ANTES de mover, senão o índice já esqueceu
        var entrantes = await indice.BacklinksAsync(de, ct);

        await repositorio.MoverAsync(de, para, ct);
        await indice.RenomearAsync(de, para, ct);
        await historico.RenomearAsync(de, para, ct);

        var corrigidas = 0;
        foreach (var origem in entrantes.Select(l => l.Origem).Distinct())
        {
            var nota = await repositorio.LerAsync(origem, ct);
            if (nota is null) continue;

            var novoTexto = ReescritorDeLigacoes.Reescrever(nota.Conteudo, de, para);
            if (novoTexto == nota.Conteudo) continue;

            await historico.ArquivarAsync(origem, nota.Conteudo, autor: null, ct);
            await repositorio.GravarAsync(origem, novoTexto, ct);
            corrigidas++;
        }

        // Reindexa o vault inteiro: mover uma nota muda a resolução de wikilinks que nem a citavam (o
        // desempate por proximidade depende de onde cada nota está). Reindexar só as tocadas deixaria o
        // grafo sutilmente errado — o tipo de erro que ninguém percebe e todo mundo sofre.
        await reconciliacao.ExecutarAsync(ct);

        var renomeada = await repositorio.LerAsync(para, ct);
        log.LogInformation("Nota renomeada: {De} → {Para} ({Corrigidas} notas com ligações reescritas).", de, para, corrigidas);
        return renomeada is null
            ? Resultado<Nota>.NaoEncontrada($"A nota \"{para}\"")
            : Resultado<Nota>.Sucesso(renomeada);
    }

    // —— VERSÕES E RECUPERAÇÃO ————————————————————————————————————————————————————————
    //
    // O histórico JÁ GUARDAVA tudo isto — cada salvamento e cada exclusão arquivam a versão anterior —
    // mas guardava sem porta: a promessa "quem apagou às onze recupera às nove" estava escrita num
    // comentário e não existia em tela nenhuma. Estes três métodos são a porta.

    /// <summary>As versões arquivadas da nota, mais recente primeiro.</summary>
    public Task<IReadOnlyList<Revisao>> VersoesAsync(CaminhoNota caminho, int limite = 50, CancellationToken ct = default) =>
        historico.ListarAsync(caminho, limite, ct);

    /// <summary>As notas que foram apagadas e ainda têm a última versão guardada.</summary>
    public async Task<IReadOnlyList<RevisaoResumida>> ApagadasAsync(CancellationToken ct = default)
    {
        var ultimas = await historico.UltimaDeCadaAsync(ct);
        if (ultimas.Count == 0) return [];

        // "Apagada" = o histórico conhece e o ÍNDICE não. O índice é a foto do disco; comparar com ele
        // evita uma ida ao disco por linha do histórico.
        var vivas = (await indice.NotasConhecidasAsync(ct)).Select(n => n.Caminho).ToHashSet();
        return [.. ultimas.Where(r => !vivas.Contains(r.Caminho))];
    }

    /// <summary>
    /// Volta a nota para o conteúdo de uma revisão arquivada — tanto a nota que existe (desfazer uma
    /// edição ruim) quanto a que foi apagada (recuperá-la).
    ///
    /// RESTAURAR NÃO APAGA HISTÓRIA: quando a nota existe, o conteúdo atual é arquivado antes de ser
    /// sobrescrito (é o SalvarAsync de sempre), então restaurar a versão errada também tem volta.
    /// </summary>
    public async Task<Resultado<Nota>> RestaurarAsync(CaminhoNota caminho, long idDaRevisao, CancellationToken ct = default)
    {
        var revisao = await historico.ObterAsync(idDaRevisao, ct);
        // A revisão tem de ser DESTA nota: restaurar por id de outra escreveria o texto de uma nota
        // dentro da outra — e o id vem da tela, que pode estar velha.
        if (revisao is null || !revisao.Caminho.Equals(caminho))
            return Resultado<Nota>.NaoEncontrada($"A versão pedida de \"{caminho}\"");

        var atual = await repositorio.LerAsync(caminho, ct);
        if (atual is not null)
        {
            // Impressão do que acabou de ser lido: se alguém escrever entre a leitura e a gravação, o
            // conflito é recusado como em qualquer salvamento — restaurar não atropela edição viva.
            var salva = await SalvarAsync(caminho, revisao.Conteudo, atual.Impressao, autor: null, ct);
            if (salva.Ok) log.LogInformation("Nota {Caminho} restaurada para a versão {Id}.", caminho, idDaRevisao);
            return salva;
        }

        // A nota foi apagada: recuperar é criá-la de novo com o conteúdo guardado — pelo CriarAsync,
        // que reindexa e conserta as ligações que esperavam por ela.
        var criada = await CriarAsync(caminho, revisao.Conteudo, ct);
        if (criada.Ok) log.LogInformation("Nota {Caminho} recuperada da exclusão (versão {Id}).", caminho, idDaRevisao);
        return criada;
    }

    public async Task<Resultado> ApagarAsync(CaminhoNota caminho, CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado.NaoEncontrada($"A nota \"{caminho}\"");

        // O histórico guarda a última versão: apagar do vault não pode significar apagar da memória. Quem
        // apagou por engano às onze da noite tem de conseguir recuperar o texto às nove da manhã.
        await historico.ArquivarAsync(caminho, nota.Conteudo, autor: null, ct);
        await repositorio.ApagarAsync(caminho, ct);
        await indice.RemoverAsync(caminho, ct);
        log.LogInformation("Nota apagada: {Caminho} (a última versão ficou no histórico).", caminho);
        return Resultado.Sucesso;
    }

    /// <summary>Reindexa uma nota só, montando o resolvedor a partir do que o índice já conhece.</summary>
    private async Task ReindexarUmaAsync(CaminhoNota caminho, CancellationToken ct)
    {
        var conhecidas = await indice.NotasConhecidasAsync(ct);
        // a própria nota pode ser nova para o índice — sem ela no mapa, um autolink não resolveria
        if (!conhecidas.Any(n => n.Caminho.Equals(caminho)))
            conhecidas = [.. conhecidas, NotaConhecida.De(caminho)];

        await reconciliacao.ReindexarAsync(caminho, new ResolvedorDeWikilinks(conhecidas), ct);
    }

    /// <summary>
    /// Reindexa quem tinha ligação quebrada que agora resolve. Chamado depois de CRIAR e de RENOMEAR —
    /// os dois momentos em que um caminho novo passa a existir no vault.
    ///
    /// O CUSTO É PEQUENO POR DEFINIÇÃO: ligação quebrada é o que a pessoa ainda não escreveu, e criar ou
    /// mover nota não é gesto de digitação — não passa por aqui a cada tecla, como o autosave passa.
    /// </summary>
    private async Task ConsertarQuemEsperavaAsync(CancellationToken ct)
    {
        var conhecidas = await indice.NotasConhecidasAsync(ct);
        await reconciliacao.ConsertarLigacoesQuebradasAsync(new ResolvedorDeWikilinks(conhecidas), ct);
    }

    public Task<IReadOnlyList<Acerto>> BuscarAsync(ConsultaDeBusca consulta, CancellationToken ct = default) =>
        indice.BuscarAsync(consulta, ct);

    public Task<IReadOnlyList<NotaIndexada>> RecentesAsync(int limite = 15, CancellationToken ct = default) =>
        indice.RecentesAsync(limite, ct);

    public Task<IReadOnlyList<EtiquetaContada>> EtiquetasAsync(CancellationToken ct = default) =>
        indice.EtiquetasAsync(ct);

    public Task<IReadOnlyList<Revisao>> HistoricoAsync(CaminhoNota caminho, CancellationToken ct = default) =>
        historico.ListarAsync(caminho, 50, ct);

    /// <summary>Data e hora do relógio injetado — a interface não deve chamar DateTime.Now direto.</summary>
    public DateTimeOffset Agora => relogio.Agora;
}
