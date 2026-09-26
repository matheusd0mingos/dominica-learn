using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

/// <summary>
/// O LINK PÚBLICO DE UMA NOTA — o único ponto do Learn em que conteúdo do vault sai sem login.
///
/// O que se prova aqui é o que nem o domínio nem a tela conseguem provar sozinhos:
///
///   1. que o lado ANÔNIMO nunca pergunta quem está logado — do outro lado do link não há ninguém, e uma
///      chamada ao IUsuarioAtual ali é uma exceção em produção na primeira visita de fora;
///
///   2. que ele lê o vault DO DONO DO LINK, e não o de quem quer que esteja na sessão — porque ler o
///      vault errado não dá erro: entrega uma nota plausível de outra pessoa;
///
///   3. que o lado AUTENTICADO nunca aceita um dono vindo de parâmetro — publicar e revogar são atos de
///      quem está logado, e receber o dono de fora seria publicar nota alheia digitando outro apelido.
///
/// Os dublês contam as chamadas de propósito: "devolveu null" também seria verdade numa implementação
/// que lê a nota alheia e joga fora o resultado.
/// </summary>
public class ServicoDeLinksDeNotaTests
{
    private static CaminhoNota Caminho(string v) => CaminhoNota.De(v);

    private sealed record Cenario(
        ServicoDeLinksDeNota Servico,
        LinksEmMemoria Links,
        VaultsDeTodos Vaults,
        LeituraEspia Leitura,
        RenderizadorDeMentira Renderizador,
        UsuarioQueTalvezNaoExista Usuario);

    private static Cenario Montar(string? eu = "matheus", string vault = "estudo")
    {
        var relogio = new RelogioFixo(DateTimeOffset.UnixEpoch);
        var vaults = new VaultsDeTodos(relogio);
        var usuario = new UsuarioQueTalvezNaoExista(eu, vault);
        var leitura = new LeituraEspia(vaults);
        var links = new LinksEmMemoria();
        var renderizador = new RenderizadorDeMentira();

        // O repositório "meu" é o do usuário logado — é o que o lado autenticado enxerga, e é assim que
        // a aplicação de verdade o resolve (filtro global por (Usuario, Vault)).
        var meu = eu is null ? vaults.De("ninguém", vault) : vaults.De(eu, vault);

        return new Cenario(
            new ServicoDeLinksDeNota(links, meu, renderizador, leitura, usuario, relogio,
                NullLogger<ServicoDeLinksDeNota>.Instance),
            links, vaults, leitura, renderizador, usuario);
    }

    // —— O LADO ANÔNIMO ————————————————————————————————————————————————————————————————

    /// <summary>
    /// O TESTE QUE SUSTENTA O RECURSO INTEIRO: abrir um link não pode encostar no usuário logado.
    ///
    /// O dublê de usuário LANÇA quando não há ninguém — que é exatamente o que o UsuarioAtualDoCircuito
    /// faz numa requisição sem circuito e sem sessão. Se um dia alguém puser um <c>await usuario.…</c>
    /// no caminho anônimo (para registrar quem visitou, por exemplo), este teste explode. Sem ele, o
    /// defeito só apareceria em produção, na primeira pessoa de fora que clicasse no link.
    /// </summary>
    [Fact]
    public async Task Abrir_o_link_nao_pergunta_quem_esta_logado()
    {
        var c = Montar(eu: null);   // ninguém logado — como numa visita de fora
        var token = c.Links.Publicar("matheus", "estudo", "Contabilidade/Aula01.md");
        c.Vaults.Escrever("matheus", "estudo", "Contabilidade/Aula01.md", "# Aula 1\n\nconteúdo");

        var nota = await c.Servico.AbrirAsync(token);

        Assert.NotNull(nota);
        Assert.Equal("Aula 1", nota.Titulo);
        Assert.Equal(0, c.Usuario.Perguntas);   // NENHUMA — ver o resumo acima
    }

    /// <summary>
    /// LÊ O VAULT DO DONO DO LINK, e não o de quem está na sessão. É o erro que não dá erro: com o
    /// vault errado, a página abre normalmente e mostra a nota de outra pessoa que por acaso tem o mesmo
    /// caminho — "Contabilidade/Aula01.md" existe no vault de todo mundo.
    /// </summary>
    [Fact]
    public async Task Le_o_vault_do_dono_do_link_e_nao_o_de_quem_esta_na_sessao()
    {
        var c = Montar(eu: "rodrigo");
        c.Vaults.Escrever("rodrigo", "estudo", "Aula01.md", "a nota do RODRIGO");
        c.Vaults.Escrever("matheus", "estudo", "Aula01.md", "a nota do MATHEUS");
        var token = c.Links.Publicar("matheus", "estudo", "Aula01.md");

        var nota = await c.Servico.AbrirAsync(token);

        Assert.NotNull(nota);
        Assert.Contains("MATHEUS", nota.Html);
        Assert.DoesNotContain("RODRIGO", nota.Html);
        Assert.Equal(("matheus", "estudo"), c.Leitura.Ultima);
    }

    /// <summary>
    /// O VAULT FAZ PARTE DO LINK. Publicar do vault de estudo não pode entregar a nota homônima do vault
    /// de trabalho — e este é o caso que um adaptador com um `&&` a menos entrega calado.
    /// </summary>
    [Fact]
    public async Task O_vault_do_link_e_respeitado()
    {
        var c = Montar(eu: null);
        c.Vaults.Escrever("matheus", "estudo", "Aula01.md", "do vault de ESTUDO");
        c.Vaults.Escrever("matheus", "trabalho", "Aula01.md", "do vault de TRABALHO");
        var token = c.Links.Publicar("matheus", "trabalho", "Aula01.md");

        var nota = await c.Servico.AbrirAsync(token);

        Assert.Contains("TRABALHO", nota!.Html);
    }

    /// <summary>
    /// TOKEN TORTO NÃO CHEGA AO BANCO. Sem esta guarda, /n/&lt;qualquer coisa&gt; vira uma consulta, e a
    /// rota pública passa a ser um martelo de banco de graça para qualquer varredor.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-um-token")]
    [InlineData("' OR 1=1 --")]
    public async Task Token_malformado_nem_consulta_o_repositorio(string? torto)
    {
        var c = Montar(eu: null);

        Assert.Null(await c.Servico.AbrirAsync(torto));
        Assert.Equal(0, c.Links.ConsultasPorToken);
    }

    /// <summary>Token bem-formado que ninguém publicou: consulta (não tem como saber sem consultar) e nega.</summary>
    [Fact]
    public async Task Token_que_nao_existe_devolve_null_sem_ler_vault_nenhum()
    {
        var c = Montar(eu: null);

        Assert.Null(await c.Servico.AbrirAsync(TokenDeLink.Sortear().Valor));
        Assert.Equal(1, c.Links.ConsultasPorToken);
        Assert.Equal(0, c.Leitura.Leituras);   // não chegou a atravessar a fronteira
    }

    /// <summary>
    /// REVOGADO É REVOGADO NA PRÓXIMA VISITA, e a decisão é do estado de agora — não de uma lista que a
    /// tela carregou. Quem já tinha a página aberta continua vendo o HTML que baixou; recarregar não
    /// traz mais nada, que é o máximo que um link revogável pode prometer.
    /// </summary>
    [Fact]
    public async Task Revogar_mata_o_endereco()
    {
        var c = Montar(eu: "matheus");
        c.Vaults.Escrever("matheus", "estudo", "Aula01.md", "conteúdo");
        var publicado = await c.Servico.PublicarAsync("Aula01.md");
        var token = publicado.Valor!.Token.Valor;
        Assert.NotNull(await c.Servico.AbrirAsync(token));

        Assert.True(await c.Servico.RevogarAsync(token));

        Assert.Null(await c.Servico.AbrirAsync(token));
    }

    /// <summary>
    /// A NOTA APAGADA DO DISCO É "não disponível", e não uma página quebrada. O link continua existindo
    /// — quem publicou pode ter renomeado o arquivo — e a distinção não ajudaria ninguém de fora.
    /// </summary>
    [Fact]
    public async Task Link_valido_para_nota_que_sumiu_devolve_null()
    {
        var c = Montar(eu: null);
        var token = c.Links.Publicar("matheus", "estudo", "sumiu.md");   // nada escrito no vault

        Assert.Null(await c.Servico.AbrirAsync(token));
    }

    // —— O LADO AUTENTICADO ————————————————————————————————————————————————————————————

    [Fact]
    public async Task Publicar_grava_com_o_dono_e_o_vault_de_quem_esta_logado()
    {
        var c = Montar(eu: "matheus", vault: "trabalho");
        c.Vaults.Escrever("matheus", "trabalho", "Aula01.md", "conteúdo");

        var r = await c.Servico.PublicarAsync("Aula01.md");

        Assert.True(r.Ok);
        Assert.Equal("matheus", r.Valor!.Dono.Valor);    // o dono NUNCA vem de fora
        Assert.Equal("trabalho", r.Valor.Vault.Valor);
    }

    /// <summary>
    /// PUBLICAR NOTA QUE NÃO EXISTE É RECUSADO. Um link que nasce quebrado é pior que nenhum: quem o
    /// recebesse veria "não está mais aqui" e concluiria que a nota foi apagada, enquanto quem publicou
    /// juraria ter mandado o endereço certo.
    /// </summary>
    [Fact]
    public async Task Publicar_nota_inexistente_e_recusado_sem_gravar_nada()
    {
        var c = Montar(eu: "matheus");

        var r = await c.Servico.PublicarAsync("nao/existe.md");

        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.NaoEncontrada, r.Motivo);
        Assert.Empty(c.Links.Todos);
    }

    [Fact]
    public async Task Publicar_caminho_invalido_e_recusado_sem_gravar_nada()
    {
        var c = Montar(eu: "matheus");

        var r = await c.Servico.PublicarAsync("../../etc/passwd");

        Assert.False(r.Ok);
        Assert.Empty(c.Links.Todos);
    }

    /// <summary>
    /// PUBLICAR DUAS VEZES DEVOLVE O MESMO ENDEREÇO.
    ///
    /// A alternativa é perigosa de um jeito calado: com dois tokens vivos para a mesma nota, quem
    /// revogasse "o link" pela tela mataria um e deixaria o outro no ar — e sairia dali convencido de
    /// que fechou o acesso.
    /// </summary>
    [Fact]
    public async Task Publicar_a_mesma_nota_de_novo_nao_cria_um_segundo_endereco()
    {
        var c = Montar(eu: "matheus");
        c.Vaults.Escrever("matheus", "estudo", "Aula01.md", "conteúdo");

        var primeiro = await c.Servico.PublicarAsync("Aula01.md");
        var segundo = await c.Servico.PublicarAsync("Aula01.md");

        Assert.Equal(primeiro.Valor!.Token, segundo.Valor!.Token);
        Assert.Single(c.Links.Todos);
    }

    /// <summary>
    /// REVOGAR O LINK DE OUTRA PESSOA NÃO FUNCIONA — e o caso é real, porque o token CIRCULA: é essa a
    /// natureza dele. Sem o dono no WHERE, qualquer um que recebesse um link despublicaria a nota de
    /// quem o mandou.
    /// </summary>
    [Fact]
    public async Task Nao_da_para_revogar_o_link_de_outra_pessoa()
    {
        var c = Montar(eu: "rodrigo");
        var alheio = c.Links.Publicar("matheus", "estudo", "Aula01.md");
        c.Vaults.Escrever("matheus", "estudo", "Aula01.md", "conteúdo");

        Assert.False(await c.Servico.RevogarAsync(alheio));

        Assert.NotNull(await c.Servico.AbrirAsync(alheio));   // continua no ar
    }

    /// <summary>A lista é só do dono — é ela que torna publicar reversível, e ela não pode listar o dos outros.</summary>
    [Fact]
    public async Task A_lista_do_que_publiquei_traz_so_o_meu()
    {
        var c = Montar(eu: "matheus");
        c.Links.Publicar("matheus", "estudo", "minha.md");
        c.Links.Publicar("rodrigo", "estudo", "dele.md");

        var meus = await c.Servico.MeusAsync();

        Assert.Single(meus);
        Assert.Equal("minha.md", meus[0].Caminho.Valor);
    }

    /// <summary>
    /// O QUE O VISITANTE VÊ É SÓ ESTA NOTA. Wikilink e anexo não são resolvidos de propósito: resolvê-los
    /// encheria a página de links para rotas autenticadas (o visitante cairia num login e concluiria que
    /// "o link não abriu"), e abrir aquelas rotas ao portador do token publicaria, junto com uma nota,
    /// tudo o que ela referencia.
    /// </summary>
    [Fact]
    public async Task A_nota_publicada_e_renderizada_sozinha_sem_o_vault_em_volta()
    {
        var c = Montar(eu: null);
        c.Vaults.Escrever("matheus", "estudo", "Aula01.md", "veja [[Aula02]] e ![[foto.png]]");
        c.Vaults.Escrever("matheus", "estudo", "Aula02.md", "a nota vizinha");
        var token = c.Links.Publicar("matheus", "estudo", "Aula01.md");

        await c.Servico.AbrirAsync(token);

        // O renderizador VIU os dois alvos (é o markdown da nota), mas os resolvedores que a publicação
        // lhe passou devolvem null para tudo — nada de nota vizinha transcluída, nada de anexo. O dublê
        // registra o que CONSEGUIU resolver; aqui o certo é nada.
        Assert.Contains("Aula02", c.Renderizador.LinksResolvidos);
        Assert.Empty(c.Renderizador.Transcluidos);
        Assert.Empty(c.Renderizador.AnexosResolvidos);
    }

    // —— dublês ————————————————————————————————————————————————————————————————————————

    /// <summary>
    /// O usuário logado — que PODE NÃO EXISTIR, e nesse caso lança, como o UsuarioAtualDoCircuito lança
    /// numa requisição sem circuito. Conta as perguntas: o caminho anônimo tem de fazer zero.
    /// </summary>
    private sealed class UsuarioQueTalvezNaoExista(string? apelido, string vault) : IUsuarioAtual
    {
        public int Perguntas { get; private set; }

        public Task<ApelidoDoUsuario> ApelidoAsync(CancellationToken ct = default)
        {
            Perguntas++;
            return apelido is null
                ? throw new InvalidOperationException("não há usuário nesta requisição")
                : Task.FromResult(ApelidoDoUsuario.De(apelido));
        }

        public Task<NomeDoVault> VaultAsync(CancellationToken ct = default)
        {
            Perguntas++;
            return apelido is null
                ? throw new InvalidOperationException("não há usuário nesta requisição")
                : Task.FromResult(NomeDoVault.De(vault));
        }
    }

    /// <summary>Um vault por (pessoa, vault) — é o que torna possível provar que se leu o de quem devia.</summary>
    private sealed class VaultsDeTodos(IRelogio relogio)
    {
        private readonly Dictionary<(string, string), VaultEmMemoria> _vaults = [];

        public VaultEmMemoria De(string dono, string vault)
        {
            if (!_vaults.TryGetValue((dono, vault), out var v))
                _vaults[(dono, vault)] = v = new VaultEmMemoria(relogio);
            return v;
        }

        public void Escrever(string dono, string vault, string caminho, string conteudo) =>
            De(dono, vault).Arquivos[caminho] = conteudo;
    }

    /// <summary>
    /// O mecanismo de leitura cruzada, em memória: entrega o repositório DAQUELA pessoa, como o escopo
    /// próprio faz na aplicação de verdade. Registra quem foi lido — é essa gravação que permite provar
    /// que a leitura foi ao vault certo, e não só que devolveu algo.
    /// </summary>
    private sealed class LeituraEspia(VaultsDeTodos vaults) : ILeituraComoOutraPessoa
    {
        public int Leituras { get; private set; }
        public (string Dono, string Vault)? Ultima { get; private set; }

        public async Task<T> LendoComoAsync<TServico, T>(
            ApelidoDoUsuario dono, NomeDoVault vault,
            Func<TServico, Task<T>> leitura, CancellationToken ct = default) where TServico : notnull
        {
            Leituras++;
            Ultima = (dono.Valor, vault.Valor);

            // Só o repositório de notas é resolvível por aqui — é o único serviço que o link pede. Um
            // pedido diferente é sinal de que o desenho mudou, e é melhor explodir do que devolver algo.
            if (vaults.De(dono.Valor, vault.Valor) is not TServico servico)
                throw new InvalidOperationException($"o link não deveria pedir {typeof(TServico).Name}");

            return await leitura(servico);
        }
    }

    private sealed class LinksEmMemoria : ILinksDeNota
    {
        public readonly List<LinkDeNota> Todos = [];
        public int ConsultasPorToken { get; private set; }

        /// <summary>Publica direto, sem passar pelo serviço — para montar o cenário de quem NÃO está logado.</summary>
        public string Publicar(string dono, string vault, string caminho)
        {
            var token = TokenDeLink.Sortear();
            Todos.Add(LinkDeNota.TentarCriar(
                token, ApelidoDoUsuario.De(dono), NomeDoVault.De(vault), CaminhoNota.De(caminho),
                DateTimeOffset.UnixEpoch)!);
            return token.Valor;
        }

        public Task<LinkDeNota> PublicarAsync(LinkDeNota link, CancellationToken ct = default)
        {
            var existente = Todos.FirstOrDefault(
                x => x.Dono == link.Dono && x.Vault == link.Vault && x.Caminho == link.Caminho);
            if (existente is not null) return Task.FromResult(existente);

            Todos.Add(link);
            return Task.FromResult(link);
        }

        public Task<LinkDeNota?> PorTokenAsync(TokenDeLink token, CancellationToken ct = default)
        {
            ConsultasPorToken++;
            return Task.FromResult(Todos.FirstOrDefault(x => x.Token == token));
        }

        public Task<LinkDeNota?> DaNotaAsync(
            ApelidoDoUsuario dono, NomeDoVault vault, CaminhoNota caminho, CancellationToken ct = default) =>
            Task.FromResult(Todos.FirstOrDefault(x => x.Dono == dono && x.Vault == vault && x.Caminho == caminho));

        public Task<IReadOnlyList<LinkDeNota>> MeusAsync(ApelidoDoUsuario dono, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LinkDeNota>>([.. Todos.Where(x => x.Dono == dono)]);

        public Task<bool> RevogarAsync(ApelidoDoUsuario dono, TokenDeLink token, CancellationToken ct = default) =>
            // O DONO NO FILTRO, como no adaptador de verdade — sem ele, quem recebe um link despublica a
            // nota de quem o mandou.
            Task.FromResult(Todos.RemoveAll(x => x.Token == token && x.Dono == dono) > 0);
    }
}
