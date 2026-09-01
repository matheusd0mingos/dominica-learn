using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

/// <summary>
/// TURMAS — e a invariante que elas não podem quebrar.
///
/// A turma INVERTE o convite (o professor cria, os alunos entram) e SÓ ISSO. Quem concede acesso
/// continua sendo o aluno: entrar grava um <see cref="Acompanhamento"/> comum, do vault dele para o
/// dono da turma, que ele revoga no painel quando quiser.
///
/// O QUE ESTES TESTES GUARDAM é que a turma não vira autoridade. O dia em que ela puder conceder por
/// conta própria é o dia em que entrar numa turma alheia arrasta o painel de outra pessoa junto.
/// </summary>
public class ServicoDeTurmasTests
{
    private static ApelidoDoUsuario Apelido(string v) => ApelidoDoUsuario.De(v);

    private sealed record Cenario(
        ServicoDeTurmas Turmas,
        ServicoDeAcompanhamento Acompanhamento,
        AcompanhamentosEmMemoria Permissoes,
        TurmasEmMemoria Repositorio,
        UsuarioTrocavel Usuario);

    private static Cenario Montar(string eu = "professor")
    {
        var permissoes = new AcompanhamentosEmMemoria();
        // O dublê de turmas precisa enxergar as permissões para montar o `Acesso` de cada aluno — é o
        // mesmo casamento que o adaptador de verdade faz com um JOIN.
        var repositorio = new TurmasEmMemoria { Permissoes = permissoes };
        var usuario = new UsuarioTrocavel(Apelido(eu), NomeDoVault.Padrao);
        var acompanhamento = new ServicoDeAcompanhamento(
            permissoes, new EstudoAlheioQueNaoLe(), usuario, NullLogger<ServicoDeAcompanhamento>.Instance);
        return new Cenario(
            new ServicoDeTurmas(repositorio, acompanhamento, usuario, NullLogger<ServicoDeTurmas>.Instance),
            acompanhamento, permissoes, repositorio, usuario);
    }

    // —— CRIAR ————————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Criar_gera_codigo_e_o_dono_e_quem_esta_logado()
    {
        var c = Montar(eu: "professor");

        var r = await c.Turmas.CriarAsync("Contabilidade 2026");

        Assert.True(r.Ok);
        Assert.Equal("professor", r.Valor!.Dono.Valor);
        Assert.Equal(CodigoDeTurma.Tamanho, r.Valor.Codigo.Valor.Length);
    }

    [Fact]
    public async Task Criar_sem_nome_e_recusado_sem_gravar()
    {
        var c = Montar();

        Assert.False((await c.Turmas.CriarAsync("  ")).Ok);
        Assert.Empty(c.Repositorio.Todas);
    }

    /// <summary>
    /// COLISÃO DE CÓDIGO é remota, não impossível — e o modo de falhar sem a laçada de tentativas seria
    /// criar uma turma que rouba o código de outra, mandando os alunos dela para o professor errado.
    /// </summary>
    [Fact]
    public async Task Codigo_ja_usado_faz_sortear_outro_em_vez_de_roubar_a_turma()
    {
        var c = Montar(eu: "professor");
        var primeira = (await c.Turmas.CriarAsync("Turma A")).Valor!;

        // força o próximo sorteio a cair no código já existente uma vez
        c.Repositorio.ForcarColisaoCom(primeira.Codigo, vezes: 1);
        var segunda = await c.Turmas.CriarAsync("Turma B");

        Assert.True(segunda.Ok);
        Assert.NotEqual(primeira.Codigo, segunda.Valor!.Codigo);
        Assert.Equal(2, c.Repositorio.Todas.Count);
    }

    // —— ENTRAR ———————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Entrar_matricula_e_abre_o_acesso_do_MEU_vault_para_o_dono_da_turma()
    {
        var c = Montar(eu: "professor");
        var turma = (await c.Turmas.CriarAsync("Contabilidade")).Valor!;

        c.Usuario.Virar(Apelido("aluno"), NomeDoVault.De("estudo"));
        var r = await c.Turmas.EntrarAsync(turma.Codigo.Valor);

        Assert.True(r.Ok);
        // O ACESSO É DO ALUNO PARA O PROFESSOR — nunca o contrário, e nunca de terceiros.
        Assert.True(c.Permissoes.Existe(dono: "aluno", vault: "estudo", convidado: "professor"));
        Assert.False(c.Permissoes.Existe(dono: "professor", vault: "estudo", convidado: "aluno"));
        Assert.Single(c.Repositorio.Matriculas);
    }

    [Fact]
    public async Task Entrar_na_propria_turma_e_recusado()
    {
        var c = Montar(eu: "professor");
        var turma = (await c.Turmas.CriarAsync("Minha")).Valor!;

        var r = await c.Turmas.EntrarAsync(turma.Codigo.Valor);

        Assert.False(r.Ok);
        Assert.Empty(c.Permissoes.Todos);
    }

    [Fact]
    public async Task Entrar_com_codigo_que_nao_existe_nao_grava_nada()
    {
        var c = Montar(eu: "aluno");

        var r = await c.Turmas.EntrarAsync("ZZZZ9999");

        Assert.False(r.Ok);
        Assert.Empty(c.Permissoes.Todos);
        Assert.Empty(c.Repositorio.Matriculas);
    }

    /// <summary>
    /// REENTRAR DEPOIS DE TROCAR DE VAULT aponta o acompanhamento para o vault NOVO. Manter o antigo
    /// faria o professor abrir um painel que o aluno não quis mais mostrar, achando que era o certo.
    /// </summary>
    [Fact]
    public async Task Reentrar_de_outro_vault_atualiza_a_matricula()
    {
        var c = Montar(eu: "professor");
        var turma = (await c.Turmas.CriarAsync("Contabilidade")).Valor!;

        c.Usuario.Virar(Apelido("aluno"), NomeDoVault.De("estudo"));
        await c.Turmas.EntrarAsync(turma.Codigo.Valor);
        c.Usuario.Virar(Apelido("aluno"), NomeDoVault.De("trabalho"));
        await c.Turmas.EntrarAsync(turma.Codigo.Valor);

        Assert.Single(c.Repositorio.Matriculas);
        Assert.Equal("trabalho", c.Repositorio.Matriculas[0].vault);
    }

    // —— SAIR ————————————————————————————————————————————————————————————————————

    /// <summary>
    /// SAIR FECHA O ACESSO JUNTO. Tirar só a matrícula deixaria o professor vendo o painel de alguém
    /// que acha que saiu — a pior forma de errar aqui, porque a pessoa fica tranquila por ter agido.
    /// </summary>
    [Fact]
    public async Task Sair_tira_a_matricula_E_o_acesso()
    {
        var c = Montar(eu: "professor");
        var turma = (await c.Turmas.CriarAsync("Contabilidade")).Valor!;
        c.Usuario.Virar(Apelido("aluno"), NomeDoVault.De("estudo"));
        await c.Turmas.EntrarAsync(turma.Codigo.Valor);

        Assert.True(await c.Turmas.SairAsync(turma.Codigo.Valor));

        Assert.Empty(c.Repositorio.Matriculas);
        Assert.False(c.Permissoes.Existe("aluno", "estudo", "professor"));
    }

    // —— LISTA DO PROFESSOR ————————————————————————————————————————————————————————

    [Fact]
    public async Task A_lista_de_alunos_e_so_do_dono_da_turma()
    {
        var c = Montar(eu: "professor");
        var turma = (await c.Turmas.CriarAsync("Contabilidade")).Valor!;

        c.Usuario.Virar(Apelido("intruso"), NomeDoVault.Padrao);
        // Null e não lista vazia: a lista diz quem estuda com quem, e isso já é informação sobre gente.
        Assert.Null(await c.Turmas.AlunosAsync(turma.Codigo.Valor));
    }

    /// <summary>
    /// A LISTA DIZ A VERDADE SOBRE O ACESSO. O aluno que revogou continua matriculado; mostrar o nome
    /// como se estivesse tudo bem faria o professor clicar e tomar "sem acesso" sem entender.
    /// </summary>
    [Fact]
    public async Task Aluno_que_revogou_aparece_na_lista_marcado_como_sem_acesso()
    {
        var c = Montar(eu: "professor");
        var turma = (await c.Turmas.CriarAsync("Contabilidade")).Valor!;
        c.Usuario.Virar(Apelido("aluno"), NomeDoVault.De("estudo"));
        await c.Turmas.EntrarAsync(turma.Codigo.Valor);
        await c.Acompanhamento.RevogarAsync("professor");   // o aluno fecha a porta pelo painel dele

        c.Usuario.Virar(Apelido("professor"), NomeDoVault.Padrao);
        var alunos = await c.Turmas.AlunosAsync(turma.Codigo.Valor);

        var unico = Assert.Single(alunos!);
        Assert.Equal("aluno", unico.Aluno.Valor);
        Assert.False(unico.Acesso);
    }

    // —— APAGAR ———————————————————————————————————————————————————————————————————

    /// <summary>
    /// ENCERRAR A TURMA TEM DE SIGNIFICAR "NÃO VEJO MAIS". Apagar só o agrupamento deixaria o professor
    /// vendo os painéis sem nenhuma lista onde isso apareça — nem ele nem os alunos notariam.
    /// </summary>
    [Fact]
    public async Task Apagar_a_turma_faz_o_dono_abrir_mao_dos_acessos()
    {
        var c = Montar(eu: "professor");
        var turma = (await c.Turmas.CriarAsync("Contabilidade")).Valor!;
        c.Usuario.Virar(Apelido("aluno"), NomeDoVault.De("estudo"));
        await c.Turmas.EntrarAsync(turma.Codigo.Valor);

        c.Usuario.Virar(Apelido("professor"), NomeDoVault.Padrao);
        Assert.True(await c.Turmas.ApagarAsync(turma.Codigo.Valor));

        Assert.Empty(c.Repositorio.Todas);
        Assert.False(c.Permissoes.Existe("aluno", "estudo", "professor"));
    }

    [Fact]
    public async Task Apagar_turma_de_outra_pessoa_nao_apaga_nada()
    {
        var c = Montar(eu: "professor");
        var turma = (await c.Turmas.CriarAsync("Contabilidade")).Valor!;

        c.Usuario.Virar(Apelido("intruso"), NomeDoVault.Padrao);
        Assert.False(await c.Turmas.ApagarAsync(turma.Codigo.Valor));
        Assert.Single(c.Repositorio.Todas);
    }

    // —— dublês ————————————————————————————————————————————————————————————————————

    /// <summary>Quem está logado muda no meio do teste — é assim que se encena aluno e professor.</summary>
    private sealed class UsuarioTrocavel(ApelidoDoUsuario apelido, NomeDoVault vault) : IUsuarioAtual
    {
        private ApelidoDoUsuario _apelido = apelido;
        private NomeDoVault _vault = vault;

        public void Virar(ApelidoDoUsuario quem, NomeDoVault onde) => (_apelido, _vault) = (quem, onde);

        public Task<ApelidoDoUsuario> ApelidoAsync(CancellationToken ct = default) => Task.FromResult(_apelido);
        public Task<NomeDoVault> VaultAsync(CancellationToken ct = default) => Task.FromResult(_vault);
    }

    private sealed class TurmasEmMemoria : ITurmas
    {
        public readonly List<Turma> Todas = [];
        public readonly List<(string turma, string aluno, string vault, DateTimeOffset em)> Matriculas = [];
        private CodigoDeTurma? _colisao;
        private int _colisoesRestantes;

        /// <summary>Faz o próximo `PorCodigoAsync` responder "já existe" para forçar novo sorteio.</summary>
        public void ForcarColisaoCom(CodigoDeTurma codigo, int vezes)
        {
            _colisao = codigo;
            _colisoesRestantes = vezes;
        }

        public Task CriarAsync(Turma turma, CancellationToken ct = default)
        {
            Todas.Add(turma);
            return Task.CompletedTask;
        }

        public Task<Turma?> PorCodigoAsync(CodigoDeTurma codigo, CancellationToken ct = default)
        {
            if (_colisoesRestantes > 0 && _colisao is not null)
            {
                _colisoesRestantes--;
                return Task.FromResult<Turma?>(Todas.FirstOrDefault(t => t.Codigo == _colisao));
            }
            return Task.FromResult(Todas.FirstOrDefault(t => t.Codigo == codigo));
        }

        public Task<IReadOnlyList<Turma>> DeQuemAsync(ApelidoDoUsuario dono, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Turma>>([.. Todas.Where(t => t.Dono == dono)]);

        public Task<IReadOnlyList<Turma>> EmQueEstouAsync(ApelidoDoUsuario aluno, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Turma>>(
                [.. Matriculas.Where(m => m.aluno == aluno.Valor)
                    .Select(m => Todas.FirstOrDefault(t => t.Codigo.Valor == m.turma))
                    .Where(t => t is not null)!]);

        public Task MatricularAsync(CodigoDeTurma codigo, ApelidoDoUsuario aluno, NomeDoVault vault, CancellationToken ct = default)
        {
            var i = Matriculas.FindIndex(m => m.turma == codigo.Valor && m.aluno == aluno.Valor);
            if (i >= 0) Matriculas[i] = Matriculas[i] with { vault = vault.Valor };
            else Matriculas.Add((codigo.Valor, aluno.Valor, vault.Valor, DateTimeOffset.UnixEpoch));
            return Task.CompletedTask;
        }

        public Task<bool> DesmatricularAsync(CodigoDeTurma codigo, ApelidoDoUsuario aluno, CancellationToken ct = default) =>
            Task.FromResult(Matriculas.RemoveAll(m => m.turma == codigo.Valor && m.aluno == aluno.Valor) > 0);

        public Task<IReadOnlyList<AlunoDaTurma>> AlunosAsync(CodigoDeTurma codigo, CancellationToken ct = default)
        {
            var turma = Todas.FirstOrDefault(t => t.Codigo == codigo);
            if (turma is null) return Task.FromResult<IReadOnlyList<AlunoDaTurma>>([]);

            return Task.FromResult<IReadOnlyList<AlunoDaTurma>>(
                [.. Matriculas.Where(m => m.turma == codigo.Valor).Select(m =>
                    new AlunoDaTurma(
                        ApelidoDoUsuario.De(m.aluno), NomeDoVault.De(m.vault), m.em,
                        Acesso: Dono(m).Contains(turma.Dono.Valor)))]);

            IEnumerable<string> Dono((string turma, string aluno, string vault, DateTimeOffset em) m) =>
                Permissoes is null ? [] : Permissoes.Convidados(m.aluno, m.vault);
        }

        public Task<bool> ApagarAsync(CodigoDeTurma codigo, ApelidoDoUsuario dono, CancellationToken ct = default)
        {
            var quantas = Todas.RemoveAll(t => t.Codigo == codigo && t.Dono == dono);
            if (quantas == 0) return Task.FromResult(false);
            Matriculas.RemoveAll(m => m.turma == codigo.Valor);
            return Task.FromResult(true);
        }

        /// <summary>Ligado pelo cenário: o dublê de turmas precisa saber quem tem acesso para montar o `Acesso`.</summary>
        public AcompanhamentosEmMemoria? Permissoes { get; set; }
    }

    private sealed class AcompanhamentosEmMemoria : IAcompanhamentosDeEstudo
    {
        public readonly List<Acompanhamento> Todos = [];

        public bool Existe(string dono, string vault, string convidado) =>
            Todos.Any(a => a.Dono.Valor == dono && a.Vault.Valor == vault && a.Convidado.Valor == convidado);

        public IEnumerable<string> Convidados(string dono, string vault) =>
            Todos.Where(a => a.Dono.Valor == dono && a.Vault.Valor == vault).Select(a => a.Convidado.Valor);

        public Task ConcederAsync(Acompanhamento a, CancellationToken ct = default)
        {
            if (!Todos.Contains(a)) Todos.Add(a);
            return Task.CompletedTask;
        }

        public Task<bool> RevogarAsync(Acompanhamento a, CancellationToken ct = default) => Task.FromResult(Todos.Remove(a));

        public Task<IReadOnlyList<Acompanhamento>> QuemMeAcompanhaAsync(
            ApelidoDoUsuario dono, NomeDoVault vault, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Acompanhamento>>([.. Todos.Where(a => a.Dono == dono && a.Vault == vault)]);

        public Task<IReadOnlyList<Acompanhamento>> QueEuAcompanhoAsync(
            ApelidoDoUsuario convidado, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Acompanhamento>>([.. Todos.Where(a => a.Convidado == convidado)]);

        public Task<bool> PodeVerAsync(
            ApelidoDoUsuario convidado, ApelidoDoUsuario dono, NomeDoVault vault, CancellationToken ct = default) =>
            Task.FromResult(Todos.Any(a => a.Convidado == convidado && a.Dono == dono && a.Vault == vault));

        public Task<int> NovidadesAsync(ApelidoDoUsuario convidado, CancellationToken ct = default) => Task.FromResult(0);
        public Task MarcarVistasAsync(ApelidoDoUsuario convidado, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Turmas não leem painel nenhum — este dublê existe só para o serviço poder ser construído.</summary>
    private sealed class EstudoAlheioQueNaoLe : ILeituraComoOutraPessoa
    {
        public Task<T> LendoComoAsync<TServico, T>(
            ApelidoDoUsuario dono, NomeDoVault vault,
            Func<TServico, Task<T>> leitura, CancellationToken ct = default) where TServico : notnull =>
            throw new InvalidOperationException("turma não lê painel — se chegou aqui, o desenho mudou");
    }
}
