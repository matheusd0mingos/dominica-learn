using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Anexos;
using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Tests;

/// <summary>
/// OS DUBLÊS DA CAMADA DE APLICAÇÃO, num arquivo só.
///
/// Eles nasceram privados dentro do ServicoDeNotasTests, e foi por isso que os outros quatro casos de
/// uso ficaram sem teste nenhum por tanto tempo: escrever o primeiro teste de qualquer um deles exigia
/// recriar disco, índice e histórico do zero. O custo de entrada era a barreira, não a dificuldade.
///
/// SÃO DUBLÊS COM COMPORTAMENTO, e não mocks que devolvem constante. O índice em memória de verdade
/// indexa, busca por etiqueta e sabe quem aponta para quem — porque o que se testa aqui é a
/// ORQUESTRAÇÃO, e orquestração sobre um dublê burro prova só que o método foi chamado.
///
/// NADA DE DISCO NEM DE BANCO: se rodar exigisse Postgres, ninguém rodaria.
/// </summary>
public sealed class RelogioFixo(DateTimeOffset agora) : IRelogio
{
    public DateTimeOffset Agora { get; set; } = agora;

    /// <summary>Anda com o relógio. É como se testa "amanhã" sem esperar até amanhã.</summary>
    public void Avancar(TimeSpan quanto) => Agora += quanto;
}

public sealed class VaultEmMemoria(IRelogio relogio) : IRepositorioDeNotas
{
    public readonly Dictionary<string, string> Arquivos = new(StringComparer.Ordinal);

    public Task<bool> ExisteAsync(CaminhoNota c, CancellationToken ct = default) =>
        Task.FromResult(Arquivos.ContainsKey(c.Valor));

    public Task<Nota?> LerAsync(CaminhoNota c, CancellationToken ct = default) =>
        Task.FromResult(Arquivos.TryGetValue(c.Valor, out var texto)
            ? Nota.Criar(c, texto, relogio.Agora) : null);

    public Task<Nota> GravarAsync(CaminhoNota c, string conteudo, CancellationToken ct = default)
    {
        Arquivos[c.Valor] = conteudo;
        return Task.FromResult(Nota.Criar(c, conteudo, relogio.Agora));
    }

    public Task MoverAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
    {
        Arquivos[para.Valor] = Arquivos[de.Valor];
        Arquivos.Remove(de.Valor);
        return Task.CompletedTask;
    }

    public Task ApagarAsync(CaminhoNota c, CancellationToken ct = default)
    {
        Arquivos.Remove(c.Valor);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<EstadoDaNota> VarrerAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var (caminho, texto) in Arquivos.ToList())
            yield return new EstadoDaNota(CaminhoNota.De(caminho), relogio.Agora, ImpressaoDigital.De(texto));
        await Task.CompletedTask;
    }
}

public sealed class IndiceEmMemoria : IIndiceDoVault
{
    public readonly Dictionary<string, (Nota Nota, IReadOnlyList<LigacaoResolvida> Ligacoes)> Notas =
        new(StringComparer.Ordinal);

    public Task<IReadOnlyList<EstadoDaNota>> EstadoAtualAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EstadoDaNota>>(
            [.. Notas.Values.Select(v => new EstadoDaNota(v.Nota.Caminho, v.Nota.ModificadoEm, v.Nota.Impressao))]);

    public Task IndexarAsync(Nota nota, IReadOnlyList<LigacaoResolvida> ligacoes, CancellationToken ct = default)
    {
        Notas[nota.Caminho.Valor] = (nota, ligacoes);
        return Task.CompletedTask;
    }

    public Task RemoverAsync(CaminhoNota c, CancellationToken ct = default)
    {
        Notas.Remove(c.Valor);
        return Task.CompletedTask;
    }

    public Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
    {
        if (Notas.Remove(de.Valor, out var v)) Notas[para.Valor] = v;
        return Task.CompletedTask;
    }

    public Task<NotaIndexada?> ObterAsync(CaminhoNota c, CancellationToken ct = default) =>
        Task.FromResult(Notas.TryGetValue(c.Valor, out var v) ? Indexada(v.Nota) : null);

    public Task<IReadOnlyList<NotaConhecida>> NotasConhecidasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NotaConhecida>>(
            [.. Notas.Values.Select(v => new NotaConhecida(v.Nota.Caminho, v.Nota.Analise.Apelidos))]);

    /// <summary>
    /// BUSCA DE VERDADE, e não lista vazia. Um índice que nunca acha nada faria o teste de "criar
    /// matéria" passar sem que a matéria fosse encontrável — que é exatamente o que ele deveria provar.
    /// </summary>
    public Task<IReadOnlyList<Acerto>> BuscarAsync(ConsultaDeBusca q, CancellationToken ct = default)
    {
        var notas = Notas.Values.Select(v => v.Nota);

        if (q.Pasta is { Length: > 0 } pasta)
            notas = notas.Where(n => n.Caminho.Pasta.Equals(pasta, StringComparison.OrdinalIgnoreCase)
                                     || n.Caminho.Pasta.StartsWith(pasta + "/", StringComparison.OrdinalIgnoreCase));

        // A hierarquia da etiqueta vale aqui como vale no Postgres: escolher #direito traz o que está
        // marcado só como #direito/penal. Ver Etiqueta.EstaAbaixoDe.
        if (q.Etiqueta is { } etiqueta)
            notas = notas.Where(n => n.Analise.Etiquetas.Any(e => e.EstaAbaixoDe(etiqueta)));

        if (q.Texto is { Length: > 0 } texto)
            notas = notas.Where(n => n.Conteudo.Contains(texto, StringComparison.OrdinalIgnoreCase)
                                     || n.Analise.Titulo.Contains(texto, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<Acerto>>(
            [.. notas.Take(q.Limite <= 0 ? int.MaxValue : q.Limite).Select(n => new Acerto(Indexada(n), Trecho(n), 1.0))]);
    }

    public Task<IReadOnlyList<LigacaoResolvida>> BacklinksAsync(CaminhoNota c, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LigacaoResolvida>>(
            [.. Notas.Values.SelectMany(v => v.Ligacoes).Where(l => l.Destino is not null && l.Destino.Equals(c))]);

    public Task<IReadOnlyList<LigacaoResolvida>> LigacoesDeAsync(CaminhoNota c, CancellationToken ct = default) =>
        Task.FromResult(Notas.TryGetValue(c.Valor, out var v) ? v.Ligacoes : []);

    public Task<IReadOnlyList<LigacaoQuebrada>> LigacoesQuebradasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LigacaoQuebrada>>(
            [.. Notas.Values
                .SelectMany(v => v.Ligacoes)
                .Where(l => l.Quebrada)
                .Select(l => new LigacaoQuebrada(l.Origem, l.Alvo))
                .Distinct()]);

    public Task<IReadOnlyList<EtiquetaContada>> EtiquetasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EtiquetaContada>>(
            [.. Notas.Values
                .SelectMany(v => v.Nota.Analise.Etiquetas.Distinct())
                .GroupBy(e => e)
                .Select(g => new EtiquetaContada(g.Key, g.Count()))
                .OrderByDescending(e => e.Notas)
                .ThenBy(e => e.Etiqueta.Valor, StringComparer.Ordinal)]);

    public Task<IReadOnlyList<IReadOnlyList<Etiqueta>>> EtiquetasPorNotaAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IReadOnlyList<Etiqueta>>>(
            [.. Notas.Values.Select(v => (IReadOnlyList<Etiqueta>)[.. v.Nota.Analise.Etiquetas.Distinct()])]);

    public Task<IReadOnlyList<NotaIndexada>> RecentesAsync(int limite, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NotaIndexada>>(
            [.. Notas.Values.OrderByDescending(v => v.Nota.ModificadoEm).Take(limite).Select(v => Indexada(v.Nota))]);

    public Task<IReadOnlyList<CaminhoNota>> TodosOsCaminhosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CaminhoNota>>([.. Notas.Values.Select(v => v.Nota.Caminho)]);

    private static NotaIndexada Indexada(Nota n) =>
        new(n.Caminho, n.Analise.Titulo, Trecho(n), n.ModificadoEm,
            n.Conteudo.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            n.Analise.Etiquetas, n.Analise.Apelidos);

    private static string Trecho(Nota n) => n.Conteudo.Length <= 120 ? n.Conteudo : n.Conteudo[..120];
}

public sealed class HistoricoEmMemoria : IHistoricoDeNotas
{
    public readonly List<Revisao> Revisoes = [];
    private long _proximo = 1;

    public Task ArquivarAsync(CaminhoNota c, string conteudo, string? autor, CancellationToken ct = default)
    {
        Revisoes.Add(new Revisao(_proximo++, c, conteudo, DateTimeOffset.UnixEpoch, autor));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Revisao>> ListarAsync(CaminhoNota c, int limite = 50, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Revisao>>([.. Revisoes.Where(r => r.Caminho.Equals(c))]);

    public Task<Revisao?> ObterAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Revisoes.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<RevisaoResumida>> UltimaDeCadaAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RevisaoResumida>>([.. Revisoes
            .GroupBy(r => r.Caminho)
            .Select(g => g.OrderByDescending(r => r.Id).First())
            .Select(r => new RevisaoResumida(r.Id, r.Caminho, r.Em))]);

    public Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
    {
        for (var i = 0; i < Revisoes.Count; i++)
            if (Revisoes[i].Caminho.Equals(de)) Revisoes[i] = Revisoes[i] with { Caminho = para };
        return Task.CompletedTask;
    }
}

/// <summary>
/// Renderiza sem Markdig: devolve o markdown cru e ANOTA o que foi pedido ao resolvedor. O que a
/// camada de aplicação tem para provar aqui não é HTML — é que ela passou o resolvedor certo, e que
/// wikilink quebrado e anexo inexistente chegam como null.
/// </summary>
public sealed class RenderizadorDeMentira : IRenderizadorDeMarkdown
{
    public readonly List<string> LinksResolvidos = [];
    public readonly List<string> AnexosResolvidos = [];

    /// <summary>Alvo → conteúdo que a aplicação entregou para transcluir. É a prova do contrato novo.</summary>
    public readonly Dictionary<string, string> Transcluidos = [];

    public NotaRenderizada Renderizar(
        string markdown, Func<string, CaminhoNota?> resolver, Func<string, string?>? anexos = null,
        Func<string, string?>? notas = null)
    {
        foreach (var alvo in AnalisadorDeNota.Analisar(markdown, "x.md").Ligacoes.Select(l => l.Alvo))
        {
            LinksResolvidos.Add(alvo);
            resolver(alvo);
            if (anexos is not null && alvo.Contains('.')) { AnexosResolvidos.Add(alvo); anexos(alvo); }
            if (notas is not null && notas(alvo) is { } conteudo) Transcluidos[alvo] = conteudo;
        }

        return new NotaRenderizada(markdown, TemFormulas: false, TemDiagramas: false);
    }
}

public sealed class AnexosEmMemoria : IArmazemDeAnexos
{
    public readonly Dictionary<string, byte[]> Arquivos = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> ExisteAsync(CaminhoDeAnexo c, CancellationToken ct = default) =>
        Task.FromResult(Arquivos.ContainsKey(c.Valor));

    public Task<CaminhoDeAnexo> GravarAsync(CaminhoDeAnexo c, Stream conteudo, CancellationToken ct = default)
    {
        using var m = new MemoryStream();
        conteudo.CopyTo(m);
        Arquivos[c.Valor] = m.ToArray();
        return Task.FromResult(c);
    }

    public Task<Stream?> AbrirAsync(CaminhoDeAnexo c, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(Arquivos.TryGetValue(c.Valor, out var b) ? new MemoryStream(b) : null);

    public Task<CaminhoDeAnexo?> ResolverAsync(string referencia, CancellationToken ct = default) =>
        Task.FromResult(Arquivos.Keys
            .Where(k => k.Equals(referencia, StringComparison.OrdinalIgnoreCase)
                        || k.EndsWith("/" + referencia, StringComparison.OrdinalIgnoreCase))
            .Select(k => CaminhoDeAnexo.TentarCriar(k, out var c, out _) ? c : null)
            .FirstOrDefault(c => c is not null));
}

public sealed class PreferenciasEmMemoria : IPreferenciasDoUsuario
{
    public int CartoesNovosPorDia { get; set; }
    public NomeDoVault? Vault { get; set; }

    public Task<int> CartoesNovosPorDiaAsync(CancellationToken ct = default) =>
        Task.FromResult(CartoesNovosPorDia);

    public Task DefinirCartoesNovosPorDiaAsync(int quantos, CancellationToken ct = default)
    {
        CartoesNovosPorDia = quantos;
        return Task.CompletedTask;
    }

    public Task<NomeDoVault?> VaultAtualAsync(CancellationToken ct = default) => Task.FromResult(Vault);

    public Task DefinirVaultAtualAsync(NomeDoVault vault, CancellationToken ct = default)
    {
        Vault = vault;
        return Task.CompletedTask;
    }
}

public sealed class RegistroEmMemoria : IRegistroDeEstudo
{
    public readonly List<LoteDeQuestoes> Lotes = [];
    public readonly List<SessaoDeEstudo> Sessoes = [];
    public readonly List<RevisaoDeCartao> Revisoes = [];

    public Task RegistrarQuestoesAsync(LoteDeQuestoes lote, CancellationToken ct = default)
    {
        Lotes.Add(lote);
        return Task.CompletedTask;
    }

    public Task RegistrarRevisaoAsync(RevisaoDeCartao revisao, CancellationToken ct = default)
    {
        Revisoes.Add(revisao);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RevisaoDeCartao>> RevisoesAsync(DateTimeOffset desde, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RevisaoDeCartao>>([.. Revisoes.Where(r => r.Em >= desde)]);

    public Task RegistrarSessaoAsync(SessaoDeEstudo sessao, CancellationToken ct = default)
    {
        Sessoes.Add(sessao);
        return Task.CompletedTask;
    }

    /// <summary>
    /// FILTRA POR "desde" DE VERDADE. É o corte que o serviço de desempenho usa para a janela de quatro
    /// semanas: um dublê que devolvesse tudo faria o teste da janela passar sem janela nenhuma.
    /// </summary>
    public Task<IReadOnlyList<LoteDeQuestoes>> QuestoesAsync(DateTimeOffset desde, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LoteDeQuestoes>>([.. Lotes.Where(l => l.Em >= desde)]);

    public Task<IReadOnlyList<SessaoDeEstudo>> SessoesAsync(DateTimeOffset desde, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SessaoDeEstudo>>([.. Sessoes.Where(s => s.Inicio >= desde)]);

    // O ID É A POSIÇÃO NA LISTA (1-based), e não um contador que anda a cada leitura como antes: com o
    // contador, ler a lista duas vezes devolvia ids diferentes para o mesmo lançamento — e nenhum teste
    // de apagar ou editar podia existir, porque o id que a tela viu já não valia. É a menor coisa que
    // torna o dublê capaz de exercitar a correção.
    public Task<IReadOnlyList<LancamentoRegistrado>> UltimosAsync(int limite, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LancamentoRegistrado>>(
            [.. Lotes.Select((l, i) => new LancamentoRegistrado(
                    TipoDeLancamento.Questoes, i + 1, l.Em, l.Materia, $"{l.Total} questões",
                    Total: l.Total, Acertos: l.Acertos, Tempo: l.Tempo, Fonte: l.Fonte))
                .Concat(Sessoes.Select((s, i) => new LancamentoRegistrado(
                    TipoDeLancamento.Tempo, i + 1, s.Inicio, s.Materia, $"{(int)s.Duracao.TotalMinutes} min",
                    Tempo: s.Duracao, Observacao: s.Observacao)))
                .OrderByDescending(x => x.Em)
                .Take(limite)]);

    public Task<bool> ApagarAsync(TipoDeLancamento tipo, int id, CancellationToken ct = default)
    {
        var alvo = tipo == TipoDeLancamento.Questoes ? (System.Collections.IList)Lotes : Sessoes;
        if (id < 1 || id > alvo.Count) return Task.FromResult(false);
        alvo.RemoveAt(id - 1);
        return Task.FromResult(true);
    }

    public Task<bool> AtualizarAsync(LancamentoEditado e, CancellationToken ct = default)
    {
        if (e.Tipo == TipoDeLancamento.Questoes)
        {
            if (e.Id < 1 || e.Id > Lotes.Count) return Task.FromResult(false);
            // a DATA vem do lançamento antigo: corrigir não move o estudo de dia (ver AtualizarAsync)
            var antigo = Lotes[e.Id - 1];
            if (!LoteDeQuestoes.TentarCriar(e.Materia, antigo.Em, e.Total, e.Acertos, e.Tempo, e.Fonte, out var novo, out _)
                || novo is null) return Task.FromResult(false);
            Lotes[e.Id - 1] = novo;
            return Task.FromResult(true);
        }

        if (e.Id < 1 || e.Id > Sessoes.Count) return Task.FromResult(false);
        var antiga = Sessoes[e.Id - 1];
        if (!SessaoDeEstudo.TentarCriar(e.Materia, antiga.Inicio, e.Tempo, e.Observacao, out var nova) || nova is null)
            return Task.FromResult(false);
        Sessoes[e.Id - 1] = nova;
        return Task.FromResult(true);
    }
}
