using System.Text;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Domain.Anexos;
using Dominica.Learn.Infrastructure.Vault;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Infrastructure.Tests;

// Integração com disco de verdade. O que se prova aqui não dá para provar com dublê: que o caminho
// RESOLVIDO não sai da raiz (um link simbólico atravessa qualquer validação textual), e que gravar por
// cima falha em vez de apagar o anexo de alguém.
public sealed class ArmazemDeAnexosEmDiscoTests : IDisposable
{
    private const string Apelido = "matheus";

    private readonly string _raizComum;
    private readonly string _raiz;
    private readonly ArmazemDeAnexosEmDisco _armazem;

    public ArmazemDeAnexosEmDiscoTests()
    {
        _raizComum = Path.Combine(Path.GetTempPath(), "learn-anexos-" + Guid.NewGuid().ToString("N")[..10]);
        var opcoes = Options.Create(new OpcoesDoVault { Raiz = _raizComum });
        _raiz = Path.Combine(_raizComum, Apelido, NomeDoVault.Padrao.Valor);
        _armazem = new ArmazemDeAnexosEmDisco(
            new RaizDoVaultDoUsuario(opcoes, new UsuarioDeTeste(Apelido)),
            NullLogger<ArmazemDeAnexosEmDisco>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raizComum)) Directory.Delete(_raizComum, recursive: true);
    }

    private static Stream Bytes(string texto) => new MemoryStream(Encoding.UTF8.GetBytes(texto));

    [Fact]
    public async Task Grava_dentro_do_vault_onde_o_Obsidian_vai_procurar()
    {
        var caminho = CaminhoDeAnexo.De("Anexos/foto.png");
        await _armazem.GravarAsync(caminho, Bytes("conteudo"));

        // O arquivo está na PASTA DO VAULT, não num diretório privado da aplicação: é isso que faz o
        // "![[Anexos/foto.png]]" funcionar quando a mesma pasta é aberta no Obsidian Desktop.
        Assert.True(File.Exists(Path.Combine(_raiz, "Anexos", "foto.png")));
    }

    [Fact]
    public async Task Gravar_por_cima_falha_em_vez_de_apagar_o_que_ja_estava_la()
    {
        // Esta é a garantia que o caso de uso NÃO consegue dar: entre "procurei um nome livre" e "gravei"
        // há uma janela, e quem a fecha é o modo CreateNew do sistema de arquivos.
        var caminho = CaminhoDeAnexo.De("Anexos/foto.png");
        await _armazem.GravarAsync(caminho, Bytes("original"));

        await Assert.ThrowsAsync<IOException>(() => _armazem.GravarAsync(caminho, Bytes("intruso")));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(_raiz, "Anexos", "foto.png")));
    }

    [Fact]
    public async Task Anexo_gravado_volta_byte_a_byte()
    {
        var caminho = CaminhoDeAnexo.De("Anexos/dados.pdf");
        await _armazem.GravarAsync(caminho, Bytes("píxels e acentuação"));

        await using var fluxo = await _armazem.AbrirAsync(caminho);
        using var leitor = new StreamReader(fluxo!, Encoding.UTF8);
        Assert.Equal("píxels e acentuação", await leitor.ReadToEndAsync());
    }

    [Fact]
    public async Task Anexo_inexistente_devolve_nulo_e_nao_excecao()
    {
        Assert.Null(await _armazem.AbrirAsync(CaminhoDeAnexo.De("Anexos/nunca.png")));
    }

    [Fact]
    public async Task Link_simbolico_para_fora_do_vault_nao_e_lido()
    {
        // O caso que NENHUMA validação textual pega: o caminho é perfeitamente inocente, e o que sai da
        // raiz é o alvo do link. Por isso a conferência é feita sobre o caminho RESOLVIDO.
        var fora = Path.Combine(Path.GetTempPath(), "learn-fora-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(fora);
        await File.WriteAllTextAsync(Path.Combine(fora, "segredo.png"), "não deveria sair daqui");
        try
        {
            Directory.CreateDirectory(_raiz);
            Directory.CreateSymbolicLink(Path.Combine(_raiz, "atalho"), fora);

            // O caminho em si é válido; o que o desqualifica é para onde ele aponta depois de resolvido.
            var pelaLigacao = CaminhoDeAnexo.De("atalho/segredo.png");
            Assert.Throws<UnauthorizedAccessException>(() => _armazem.AbrirAsync(pelaLigacao).GetAwaiter().GetResult());
        }
        finally
        {
            Directory.Delete(fora, recursive: true);
        }
    }

    // —— RESOLUÇÃO ————————————————————————————————————————————————————————————————————
    [Fact]
    public async Task Resolve_pelo_caminho_literal()
    {
        await _armazem.GravarAsync(CaminhoDeAnexo.De("Aulas/diagrama.png"), Bytes("x"));
        var achado = await _armazem.ResolverAsync("Aulas/diagrama.png");
        Assert.Equal("Aulas/diagrama.png", achado!.Valor);
    }

    [Fact]
    public async Task Resolve_pelo_nome_curto_na_pasta_de_anexos()
    {
        // É como o embed é escrito na prática: quem digita põe "foto.png", não "Anexos/foto.png".
        await _armazem.GravarAsync(CaminhoDeAnexo.De("Anexos/foto.png"), Bytes("x"));
        var achado = await _armazem.ResolverAsync("foto.png");
        Assert.Equal("Anexos/foto.png", achado!.Valor);
    }

    [Fact]
    public async Task Referencia_com_ponto_ponto_nao_resolve()
    {
        // Chega de um "![[../../.env]]" escrito dentro de uma nota — texto arbitrário indo parar numa
        // chamada de sistema de arquivos.
        Assert.Null(await _armazem.ResolverAsync("../../.env"));
        Assert.Null(await _armazem.ResolverAsync("Anexos/../../etc/passwd"));
    }

    [Fact]
    public async Task Nao_varre_o_vault_inteiro_atras_do_nome()
    {
        // Decisão declarada: um arquivo fora da pasta de anexos, referenciado só pelo nome curto, NÃO é
        // encontrado. O contrário custaria uma varredura de disco por imagem exibida.
        await _armazem.GravarAsync(CaminhoDeAnexo.De("Aulas/escondida.png"), Bytes("x"));
        Assert.Null(await _armazem.ResolverAsync("escondida.png"));
    }
}
