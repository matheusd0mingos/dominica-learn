using Dominica.Learn.Application;
using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

// Testes da CAMADA DE APLICAÇÃO com dublês em memória. Não tocam disco nem banco: o que se prova aqui é
// a ORQUESTRAÇÃO — que salvar arquiva antes de sobrescrever, que conflito é detectado, que renomear
// reescreve as ligações de entrada. Se isso exigisse Postgres, ninguém rodaria.
public class ServicoDeNotasTests
{
    // —— dublês ————————————————————————————————————————————————————————————————————————
    private sealed class RelogioFixo(DateTimeOffset agora) : IRelogio
    {
        public DateTimeOffset Agora { get; set; } = agora;
    }

    private sealed class VaultEmMemoria : IRepositorioDeNotas
    {
        public readonly Dictionary<string, string> Arquivos = new(StringComparer.Ordinal);
        private readonly IRelogio _relogio;
        public VaultEmMemoria(IRelogio relogio) => _relogio = relogio;

        public Task<bool> ExisteAsync(CaminhoNota c, CancellationToken ct = default) =>
            Task.FromResult(Arquivos.ContainsKey(c.Valor));

        public Task<Nota?> LerAsync(CaminhoNota c, CancellationToken ct = default) =>
            Task.FromResult(Arquivos.TryGetValue(c.Valor, out var texto)
                ? Nota.Criar(c, texto, _relogio.Agora) : null);

        public Task<Nota> GravarAsync(CaminhoNota c, string conteudo, CancellationToken ct = default)
        {
            Arquivos[c.Valor] = conteudo;
            return Task.FromResult(Nota.Criar(c, conteudo, _relogio.Agora));
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
                yield return new EstadoDaNota(CaminhoNota.De(caminho), _relogio.Agora, ImpressaoDigital.De(texto));
            await Task.CompletedTask;
        }
    }

    private sealed class IndiceEmMemoria : IIndiceDoVault
    {
        public readonly Dictionary<string, (Nota Nota, IReadOnlyList<LigacaoResolvida> Ligacoes)> Notas = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<EstadoDaNota>> EstadoAtualAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EstadoDaNota>>(
                Notas.Values.Select(v => new EstadoDaNota(v.Nota.Caminho, v.Nota.ModificadoEm, v.Nota.Impressao)).ToList());

        public Task IndexarAsync(Nota nota, IReadOnlyList<LigacaoResolvida> ligacoes, CancellationToken ct = default)
        {
            Notas[nota.Caminho.Valor] = (nota, ligacoes);
            return Task.CompletedTask;
        }

        public Task RemoverAsync(CaminhoNota c, CancellationToken ct = default) { Notas.Remove(c.Valor); return Task.CompletedTask; }

        public Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
        {
            if (Notas.Remove(de.Valor, out var v)) Notas[para.Valor] = v;
            return Task.CompletedTask;
        }

        public Task<NotaIndexada?> ObterAsync(CaminhoNota c, CancellationToken ct = default) =>
            Task.FromResult<NotaIndexada?>(null);

        public Task<IReadOnlyList<NotaConhecida>> NotasConhecidasAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotaConhecida>>(
                Notas.Values.Select(v => new NotaConhecida(v.Nota.Caminho, v.Nota.Analise.Apelidos)).ToList());

        public Task<IReadOnlyList<Acerto>> BuscarAsync(ConsultaDeBusca q, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Acerto>>([]);

        public Task<IReadOnlyList<LigacaoResolvida>> BacklinksAsync(CaminhoNota c, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LigacaoResolvida>>(
                Notas.Values.SelectMany(v => v.Ligacoes).Where(l => l.Destino is not null && l.Destino.Equals(c)).ToList());

        public Task<IReadOnlyList<LigacaoResolvida>> LigacoesDeAsync(CaminhoNota c, CancellationToken ct = default) =>
            Task.FromResult(Notas.TryGetValue(c.Valor, out var v) ? v.Ligacoes : []);

        public Task<IReadOnlyList<EtiquetaContada>> EtiquetasAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EtiquetaContada>>([]);

        public Task<IReadOnlyList<NotaIndexada>> RecentesAsync(int limite, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotaIndexada>>([]);

        public Task<IReadOnlyList<CaminhoNota>> TodosOsCaminhosAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CaminhoNota>>(Notas.Values.Select(v => v.Nota.Caminho).ToList());
    }

    private sealed class HistoricoEmMemoria : IHistoricoDeNotas
    {
        public readonly List<Revisao> Revisoes = [];
        private long _proximo = 1;

        public Task ArquivarAsync(CaminhoNota c, string conteudo, string? autor, CancellationToken ct = default)
        {
            Revisoes.Add(new Revisao(_proximo++, c, conteudo, DateTimeOffset.UnixEpoch, autor));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Revisao>> ListarAsync(CaminhoNota c, int limite = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Revisao>>(Revisoes.Where(r => r.Caminho.Equals(c)).ToList());

        public Task<Revisao?> ObterAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(Revisoes.FirstOrDefault(r => r.Id == id));

        public Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
        {
            for (var i = 0; i < Revisoes.Count; i++)
                if (Revisoes[i].Caminho.Equals(de)) Revisoes[i] = Revisoes[i] with { Caminho = para };
            return Task.CompletedTask;
        }
    }

    // —— montagem ————————————————————————————————————————————————————————————————————
    private sealed record Cenario(ServicoDeNotas Servico, VaultEmMemoria Vault, IndiceEmMemoria Indice, HistoricoEmMemoria Historico);

    private static Cenario Montar()
    {
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero));
        var vault = new VaultEmMemoria(relogio);
        var indice = new IndiceEmMemoria();
        var historico = new HistoricoEmMemoria();
        var reconciliacao = new ReconciliarVault(vault, indice, relogio, NullLogger<ReconciliarVault>.Instance);
        var servico = new ServicoDeNotas(vault, indice, historico, reconciliacao, relogio, NullLogger<ServicoDeNotas>.Instance);
        return new Cenario(servico, vault, indice, historico);
    }

    // —— testes ——————————————————————————————————————————————————————————————————————
    [Fact]
    public async Task Criar_grava_e_indexa()
    {
        var c = Montar();
        var r = await c.Servico.CriarAsync(CaminhoNota.De("Direito/Licitações.md"), "# Licitações\n\n#direito");

        Assert.True(r.Ok);
        Assert.Contains("Direito/Licitações.md", c.Vault.Arquivos.Keys);
        Assert.Contains("Direito/Licitações.md", c.Indice.Notas.Keys);
    }

    [Fact]
    public async Task Criar_recusa_quando_ja_existe()
    {
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        await c.Servico.CriarAsync(caminho, "um");

        var r = await c.Servico.CriarAsync(caminho, "outro");
        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.JaExiste, r.Motivo);
        Assert.Equal("um", c.Vault.Arquivos["A.md"]);   // não sobrescreveu
    }

    [Fact]
    public async Task Salvar_arquiva_a_versao_anterior_antes_de_sobrescrever()
    {
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        var criada = await c.Servico.CriarAsync(caminho, "versão um");

        await c.Servico.SalvarAsync(caminho, "versão dois", criada.Valor!.Impressao);

        var revisao = Assert.Single(c.Historico.Revisoes);
        Assert.Equal("versão um", revisao.Conteudo);
        Assert.Equal("versão dois", c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task Salvar_conteudo_identico_nao_escreve_nem_versiona()
    {
        // O autosave dispara a cada poucos segundos: gravar texto idêntico sujaria o histórico, mexeria
        // na data de modificação e acordaria o vigia de arquivos à toa.
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        var criada = await c.Servico.CriarAsync(caminho, "mesmo texto");

        var r = await c.Servico.SalvarAsync(caminho, "mesmo texto", criada.Valor!.Impressao);

        Assert.True(r.Ok);
        Assert.Empty(c.Historico.Revisoes);
    }

    [Fact]
    public async Task Salvar_detecta_conflito_quando_o_disco_mudou_por_fora()
    {
        // O CENÁRIO CENTRAL DO PRODUTO: o Obsidian do notebook gravou enquanto a aba estava aberta.
        // Sobrescrever em silêncio seria apagar meia hora de trabalho de alguém.
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        var criada = await c.Servico.CriarAsync(caminho, "original");
        var impressaoQueOEditorTinha = criada.Valor!.Impressao;

        c.Vault.Arquivos["A.md"] = "escrito pelo Obsidian";   // mudança externa

        var r = await c.Servico.SalvarAsync(caminho, "escrito pelo editor", impressaoQueOEditorTinha);

        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.Conflito, r.Motivo);
        Assert.Equal("escrito pelo Obsidian", c.Vault.Arquivos["A.md"]);   // preservado
    }

    [Fact]
    public async Task Salvar_sem_impressao_sobrescreve_de_proposito()
    {
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        await c.Servico.CriarAsync(caminho, "original");
        c.Vault.Arquivos["A.md"] = "externo";

        var r = await c.Servico.SalvarAsync(caminho, "eu decido", impressaoEsperada: null);

        Assert.True(r.Ok);
        Assert.Equal("eu decido", c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task Renomear_reescreve_as_ligacoes_que_apontavam_para_a_nota()
    {
        // Sem isto, renomear quebraria todas as ligações de entrada — e num vault de estudo a rede de
        // ligações É o conhecimento.
        var c = Montar();
        var alvo = CaminhoNota.De("Direito/Licitações.md");
        var origem = CaminhoNota.De("Resumo.md");
        await c.Servico.CriarAsync(alvo, "# Licitações");
        await c.Servico.CriarAsync(origem, "ver [[Licitações]] e [[Licitações|as regras]]");

        var r = await c.Servico.RenomearAsync(alvo, CaminhoNota.De("Direito/Contratos.md"));

        Assert.True(r.Ok);
        Assert.Equal("ver [[Contratos]] e [[Contratos|as regras]]", c.Vault.Arquivos["Resumo.md"]);
        Assert.DoesNotContain("Direito/Licitações.md", c.Vault.Arquivos.Keys);
    }

    [Fact]
    public async Task Renomear_leva_o_historico_junto()
    {
        var c = Montar();
        var de = CaminhoNota.De("A.md");
        var criada = await c.Servico.CriarAsync(de, "v1");
        await c.Servico.SalvarAsync(de, "v2", criada.Valor!.Impressao);

        var para = CaminhoNota.De("B.md");
        await c.Servico.RenomearAsync(de, para);

        Assert.NotEmpty(await c.Servico.HistoricoAsync(para));
        Assert.Empty(await c.Servico.HistoricoAsync(de));
    }

    [Fact]
    public async Task Renomear_recusa_destino_ocupado()
    {
        var c = Montar();
        await c.Servico.CriarAsync(CaminhoNota.De("A.md"), "a");
        await c.Servico.CriarAsync(CaminhoNota.De("B.md"), "b");

        var r = await c.Servico.RenomearAsync(CaminhoNota.De("A.md"), CaminhoNota.De("B.md"));

        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.JaExiste, r.Motivo);
        Assert.Equal("b", c.Vault.Arquivos["B.md"]);   // não sobrescreveu
    }

    [Fact]
    public async Task Apagar_guarda_a_ultima_versao_no_historico()
    {
        // Quem apagou por engano às onze da noite tem de conseguir recuperar às nove da manhã.
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        await c.Servico.CriarAsync(caminho, "conteúdo que não pode sumir");

        var r = await c.Servico.ApagarAsync(caminho);

        Assert.True(r.Ok);
        Assert.DoesNotContain("A.md", c.Vault.Arquivos.Keys);
        Assert.Contains(c.Historico.Revisoes, x => x.Conteudo == "conteúdo que não pode sumir");
    }

    [Fact]
    public async Task Abrir_traz_backlinks_e_ligacoes_quebradas()
    {
        var c = Montar();
        await c.Servico.CriarAsync(CaminhoNota.De("Alvo.md"), "# Alvo");
        await c.Servico.CriarAsync(CaminhoNota.De("Origem.md"), "aponta para [[Alvo]] e para [[NãoExiste]]");

        var alvo = await c.Servico.AbrirAsync(CaminhoNota.De("Alvo.md"));
        Assert.True(alvo.Ok);
        Assert.Single(alvo.Valor!.Backlinks);

        var origem = await c.Servico.AbrirAsync(CaminhoNota.De("Origem.md"));
        Assert.Contains(origem.Valor!.Saidas, l => l.Quebrada && l.Alvo == "NãoExiste");
    }

    [Fact]
    public async Task Abrir_nota_inexistente_devolve_falha_e_nao_excecao()
    {
        var r = await Montar().Servico.AbrirAsync(CaminhoNota.De("Nada.md"));
        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.NaoEncontrada, r.Motivo);
    }
}
