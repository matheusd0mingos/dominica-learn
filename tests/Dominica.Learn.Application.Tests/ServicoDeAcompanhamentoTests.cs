using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

/// <summary>
/// ACOMPANHAR OS ESTUDOS DE OUTRA PESSOA — e, principalmente, NÃO acompanhar quem não deixou.
///
/// O que se prova aqui é a única coisa que o domínio não consegue provar sozinho: que a autorização é
/// consultada ANTES do mecanismo de leitura, e que sem ela o mecanismo não é acionado NENHUMA VEZ.
///
/// A SEGUNDA METADE DESSA FRASE É A QUE IMPORTA. Um teste que só verifica "devolveu null" passaria por
/// uma implementação que lê o registro alheio inteiro e joga fora o resultado — o dado teria saído do
/// banco, atravessado a fronteira e ficado em memória, e a única coisa impedindo o vazamento seria a
/// tela não desenhar. Por isso o dublê CONTA as chamadas: o certo é zero.
/// </summary>
public class ServicoDeAcompanhamentoTests
{
    private static ApelidoDoUsuario Apelido(string v) => ApelidoDoUsuario.De(v);

    private sealed record Cenario(
        ServicoDeAcompanhamento Servico,
        AcompanhamentosEmMemoria Permissoes,
        EstudoAlheioEspiao Espiao);

    private static Cenario Montar(string eu = "rodrigo")
    {
        var permissoes = new AcompanhamentosEmMemoria();
        var espiao = new EstudoAlheioEspiao();
        var usuario = new UsuarioFixo(Apelido(eu), NomeDoVault.Padrao);
        return new Cenario(
            new ServicoDeAcompanhamento(permissoes, espiao, usuario, NullLogger<ServicoDeAcompanhamento>.Instance),
            permissoes, espiao);
    }

    // —— A RECUSA ——————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Sem_acompanhamento_o_painel_e_negado_e_o_registro_alheio_nao_e_nem_tocado()
    {
        var c = Montar(eu: "rodrigo");   // ninguém concedeu nada

        var painel = await c.Servico.PainelDeAsync(
            Apelido("matheus"), NomeDoVault.Padrao, d => d.ResumoAsync());

        Assert.Null(painel);
        Assert.Equal(0, c.Espiao.Leituras);   // o mecanismo NÃO foi acionado — ver o resumo desta classe
    }

    /// <summary>
    /// O VAULT FAZ PARTE DA PERMISSÃO. Quem foi convidado para o vault de estudo não vê o de trabalho —
    /// e este é o caso que uma consulta com um `&&` a menos entrega calada, mostrando números plausíveis
    /// do vault errado.
    /// </summary>
    [Fact]
    public async Task Acesso_a_um_vault_nao_da_acesso_a_outro()
    {
        var c = Montar(eu: "rodrigo");
        c.Permissoes.Conceder("matheus", "estudo", "rodrigo");

        Assert.NotNull(await c.Servico.PainelDeAsync(Apelido("matheus"), NomeDoVault.De("estudo"), d => d.ResumoAsync()));
        Assert.Null(await c.Servico.PainelDeAsync(Apelido("matheus"), NomeDoVault.De("trabalho"), d => d.ResumoAsync()));
        Assert.Equal(1, c.Espiao.Leituras);   // só a permitida leu
    }

    /// <summary>
    /// A DIREÇÃO NÃO É RECÍPROCA: matheus deixar rodrigo ver não deixa matheus ver o de rodrigo. É o
    /// erro que um `x.Dono == eu` no lugar de `x.Convidado == eu` produziria, sem dar erro nenhum.
    /// </summary>
    [Fact]
    public async Task Conceder_nao_e_reciproco()
    {
        var c = Montar(eu: "matheus");
        c.Permissoes.Conceder("matheus", "estudo", "rodrigo");   // matheus abriu para rodrigo

        var aoContrario = await c.Servico.PainelDeAsync(
            Apelido("rodrigo"), NomeDoVault.De("estudo"), d => d.ResumoAsync());

        Assert.Null(aoContrario);
        Assert.Equal(0, c.Espiao.Leituras);
    }

    /// <summary>
    /// A PERMISSÃO É CONFERIDA NO ATO, e não quando a lista foi carregada. Entre abrir a lista e clicar
    /// num nome pode haver uma revogação — e o clique tem de obedecer ao estado de agora.
    /// </summary>
    [Fact]
    public async Task Revogar_fecha_o_painel_na_leitura_seguinte()
    {
        var c = Montar(eu: "rodrigo");
        c.Permissoes.Conceder("matheus", "estudo", "rodrigo");
        Assert.NotNull(await c.Servico.PainelDeAsync(Apelido("matheus"), NomeDoVault.De("estudo"), d => d.ResumoAsync()));

        c.Permissoes.Revogar("matheus", "estudo", "rodrigo");

        Assert.Null(await c.Servico.PainelDeAsync(Apelido("matheus"), NomeDoVault.De("estudo"), d => d.ResumoAsync()));
        Assert.Equal(1, c.Espiao.Leituras);   // a segunda tentativa nem chegou ao mecanismo
    }

    // —— CONVIDAR ——————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Convidar_grava_a_permissao_com_o_dono_sendo_quem_esta_logado()
    {
        var c = Montar(eu: "matheus");

        var r = await c.Servico.ConvidarAsync("rodrigo");

        Assert.True(r.Ok);
        Assert.Equal("matheus", r.Valor!.Dono.Valor);       // o dono NUNCA vem de fora
        Assert.Equal("rodrigo", r.Valor.Convidado.Valor);
        Assert.True(c.Permissoes.Existe("matheus", "estudo", "rodrigo"));
    }

    [Fact]
    public async Task Convidar_a_si_mesmo_e_recusado_com_a_razao_por_extenso()
    {
        var c = Montar(eu: "matheus");

        var r = await c.Servico.ConvidarAsync("matheus");

        Assert.False(r.Ok);
        Assert.Contains("próprio painel", r.Mensagem);
        Assert.False(c.Permissoes.Existe("matheus", "estudo", "matheus"));
    }

    [Fact]
    public async Task Convidar_apelido_invalido_e_recusado_sem_gravar_nada()
    {
        var c = Montar(eu: "matheus");

        var r = await c.Servico.ConvidarAsync("   ");

        Assert.False(r.Ok);
        Assert.Empty(c.Permissoes.Todos);
    }

    [Fact]
    public async Task Convidar_duas_vezes_a_mesma_pessoa_nao_duplica()
    {
        var c = Montar(eu: "matheus");

        await c.Servico.ConvidarAsync("rodrigo");
        await c.Servico.ConvidarAsync("rodrigo");

        Assert.Single(c.Permissoes.Todos);
    }

    [Fact]
    public async Task Revogar_quem_nunca_teve_acesso_nao_e_erro()
    {
        var c = Montar(eu: "matheus");

        Assert.False(await c.Servico.RevogarAsync("ninguem"));
    }

    // —— dublês ————————————————————————————————————————————————————————————————————

    private sealed class UsuarioFixo(ApelidoDoUsuario apelido, NomeDoVault vault) : IUsuarioAtual
    {
        public Task<ApelidoDoUsuario> ApelidoAsync(CancellationToken ct = default) => Task.FromResult(apelido);
        public Task<NomeDoVault> VaultAsync(CancellationToken ct = default) => Task.FromResult(vault);
    }

    private sealed class AcompanhamentosEmMemoria : IAcompanhamentosDeEstudo
    {
        public readonly List<Acompanhamento> Todos = [];

        public void Conceder(string dono, string vault, string convidado) =>
            Todos.Add(Acompanhamento.TentarCriar(ApelidoDoUsuario.De(dono), NomeDoVault.De(vault), ApelidoDoUsuario.De(convidado))!);

        public void Revogar(string dono, string vault, string convidado) =>
            Todos.RemoveAll(a => a.Dono.Valor == dono && a.Vault.Valor == vault && a.Convidado.Valor == convidado);

        public bool Existe(string dono, string vault, string convidado) =>
            Todos.Any(a => a.Dono.Valor == dono && a.Vault.Valor == vault && a.Convidado.Valor == convidado);

        public Task ConcederAsync(Acompanhamento a, CancellationToken ct = default)
        {
            if (!Todos.Contains(a)) Todos.Add(a);
            return Task.CompletedTask;
        }

        public Task<bool> RevogarAsync(Acompanhamento a, CancellationToken ct = default) =>
            Task.FromResult(Todos.Remove(a));

        public Task<IReadOnlyList<Acompanhamento>> QuemMeAcompanhaAsync(
            ApelidoDoUsuario dono, NomeDoVault vault, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Acompanhamento>>(
                [.. Todos.Where(a => a.Dono == dono && a.Vault == vault)]);

        public Task<IReadOnlyList<Acompanhamento>> QueEuAcompanhoAsync(
            ApelidoDoUsuario convidado, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Acompanhamento>>([.. Todos.Where(a => a.Convidado == convidado)]);

        public Task<bool> PodeVerAsync(
            ApelidoDoUsuario convidado, ApelidoDoUsuario dono, NomeDoVault vault, CancellationToken ct = default) =>
            Task.FromResult(Todos.Any(a => a.Convidado == convidado && a.Dono == dono && a.Vault == vault));
    }

    /// <summary>
    /// CONTA AS LEITURAS, e é essa contagem que dá sentido aos testes de recusa: "devolveu null" também
    /// seria verdade numa implementação que lê o registro alheio e descarta o resultado depois.
    /// </summary>
    private sealed class EstudoAlheioEspiao : IEstudoDeOutraPessoa
    {
        public int Leituras { get; private set; }

        public Task<T> LendoComoAsync<T>(
            ApelidoDoUsuario dono, NomeDoVault vault,
            Func<ServicoDeDesempenho, Task<T>> leitura, CancellationToken ct = default)
        {
            Leituras++;
            // Não roda a leitura de verdade: o que este dublê representa é "o mecanismo foi acionado".
            // Montar um ServicoDeDesempenho aqui só para descartá-lo confundiria o que se está medindo.
            return Task.FromResult((T)(object)ResumoDeDesempenho.Vazio);
        }
    }
}
