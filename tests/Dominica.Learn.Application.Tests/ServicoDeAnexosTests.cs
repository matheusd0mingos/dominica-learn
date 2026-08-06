using System.Text;
using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Anexos;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

public class ServicoDeAnexosTests
{
    private sealed class ArmazemEmMemoria : IArmazemDeAnexos
    {
        public readonly Dictionary<string, byte[]> Arquivos = new(StringComparer.Ordinal);

        public Task<bool> ExisteAsync(CaminhoDeAnexo c, CancellationToken ct = default) =>
            Task.FromResult(Arquivos.ContainsKey(c.Valor));

        public async Task<CaminhoDeAnexo> GravarAsync(CaminhoDeAnexo c, Stream conteudo, CancellationToken ct = default)
        {
            // Espelha o CreateNew do adaptador de disco: gravar por cima é perda de dado, não sucesso.
            if (Arquivos.ContainsKey(c.Valor)) throw new IOException($"Já existe {c.Valor}.");
            using var m = new MemoryStream();
            await conteudo.CopyToAsync(m, ct);
            Arquivos[c.Valor] = m.ToArray();
            return c;
        }

        public Task<Stream?> AbrirAsync(CaminhoDeAnexo c, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Arquivos.TryGetValue(c.Valor, out var b) ? new MemoryStream(b) : null);

        public Task<CaminhoDeAnexo?> ResolverAsync(string referencia, CancellationToken ct = default)
        {
            if (CaminhoDeAnexo.TentarCriar(referencia, out var literal, out _) && literal is not null
                && Arquivos.ContainsKey(literal.Valor))
                return Task.FromResult<CaminhoDeAnexo?>(literal);

            var soNome = referencia[(referencia.LastIndexOf('/') + 1)..];
            if (CaminhoDeAnexo.TentarCriar($"{CaminhoDeAnexo.PastaPadrao}/{soNome}", out var naPasta, out _)
                && naPasta is not null && Arquivos.ContainsKey(naPasta.Valor))
                return Task.FromResult<CaminhoDeAnexo?>(naPasta);

            return Task.FromResult<CaminhoDeAnexo?>(null);
        }
    }

    private readonly ArmazemEmMemoria _armazem = new();
    private readonly ServicoDeAnexos _servico;

    public ServicoDeAnexosTests() =>
        _servico = new ServicoDeAnexos(_armazem, NullLogger<ServicoDeAnexos>.Instance);

    private static Stream Conteudo(string texto) => new MemoryStream(Encoding.UTF8.GetBytes(texto));

    private Task<Resultado<AnexoGuardado>> Anexar(string nome, string conteudo = "bytes") =>
        _servico.AnexarAsync(nome, Conteudo(conteudo), Encoding.UTF8.GetByteCount(conteudo));

    [Fact]
    public async Task Imagem_entra_no_vault_e_devolve_embed()
    {
        var r = await Anexar("foto.png");

        Assert.True(r.Ok);
        Assert.Equal("Anexos/foto.png", r.Valor!.Caminho.Valor);
        // Embed do Obsidian, NÃO uma URL desta aplicação: o .md tem de continuar valendo quando aberto
        // no Obsidian Desktop e quando este servidor não existir mais.
        Assert.Equal("![[Anexos/foto.png]]", r.Valor.Markdown);
        Assert.DoesNotContain("/anexos/", r.Valor.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_entra_como_link_e_nao_como_embed()
    {
        // Um PDF de 300 páginas embutido no meio de um resumo é uma parede. Quem quer ler, clica.
        var r = await Anexar("edital.pdf");
        Assert.Equal("[[Anexos/edital.pdf]]", r.Valor!.Markdown);
    }

    [Fact]
    public async Task Segundo_arquivo_de_mesmo_nome_nao_apaga_o_primeiro()
    {
        var primeiro = await Anexar("captura de tela.png", "primeiro");
        var segundo = await Anexar("captura de tela.png", "segundo");

        Assert.Equal("Anexos/captura de tela.png", primeiro.Valor!.Caminho.Valor);
        Assert.Equal("Anexos/captura de tela-2.png", segundo.Valor!.Caminho.Valor);
        Assert.Equal("primeiro", Encoding.UTF8.GetString(_armazem.Arquivos["Anexos/captura de tela.png"]));
    }

    [Fact]
    public async Task Terceiro_arquivo_de_mesmo_nome_continua_a_contagem()
    {
        await Anexar("nota.png");
        await Anexar("nota.png");
        var terceiro = await Anexar("nota.png");
        Assert.Equal("Anexos/nota-3.png", terceiro.Valor!.Caminho.Valor);
    }

    [Fact]
    public async Task Arquivo_grande_demais_e_recusado_antes_de_tocar_o_disco()
    {
        var r = await _servico.AnexarAsync("video.mp4", Conteudo("x"), tamanhoBytes: 30L * 1024 * 1024,
            tamanhoMaximoBytes: 25L * 1024 * 1024);

        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.Invalida, r.Motivo);
        Assert.Contains("limite", r.Mensagem);
        Assert.Empty(_armazem.Arquivos);   // nada foi gravado
    }

    [Fact]
    public async Task Arquivo_vazio_e_recusado()
    {
        var r = await _servico.AnexarAsync("vazio.png", Conteudo(""), 0);
        Assert.False(r.Ok);
        Assert.Empty(_armazem.Arquivos);
    }

    [Fact]
    public async Task Extensao_fora_da_lista_nao_chega_ao_armazem()
    {
        var r = await Anexar("payload.exe");
        Assert.False(r.Ok);
        Assert.Empty(_armazem.Arquivos);
    }

    [Fact]
    public async Task Nome_com_caminho_nao_escreve_fora_da_pasta_de_anexos()
    {
        var r = await Anexar("../../../etc/cron.png");
        Assert.True(r.Ok);
        Assert.Equal("Anexos/cron.png", r.Valor!.Caminho.Valor);
    }

    [Fact]
    public async Task Referencia_pelo_nome_curto_encontra_o_anexo_na_pasta()
    {
        // É como o embed é escrito na prática: "![[foto.png]]", não "![[Anexos/foto.png]]".
        await Anexar("foto.png");
        var achado = await _servico.ResolverAsync("foto.png");
        Assert.Equal("Anexos/foto.png", achado!.Valor);
    }

    [Fact]
    public async Task Referencia_a_arquivo_inexistente_devolve_nulo()
    {
        Assert.Null(await _servico.ResolverAsync("nunca-existiu.png"));
    }
}
