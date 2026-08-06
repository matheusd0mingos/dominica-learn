using System.Text;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Infrastructure.Vault;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Infrastructure.Tests;

// Teste de INTEGRAÇÃO com o disco de verdade (pasta temporária). O que se prova aqui não dá para provar
// com dublê: encoding do arquivo gravado, escape da raiz, e o formato que o Obsidian vai encontrar.
public sealed class RepositorioDeNotasEmDiscoTests : IDisposable
{
    private const string Apelido = "matheus";

    private readonly string _raizComum;
    private readonly string _raiz;
    private readonly RepositorioDeNotasEmDisco _repo;

    public RepositorioDeNotasEmDiscoTests()
    {
        // A raiz COMUM guarda os vaults; o do usuário é a subpasta. Os testes olham a subpasta, que é
        // onde as notas de fato ficam — e é essa diferença que separa uma pessoa da outra.
        _raizComum = Path.Combine(Path.GetTempPath(), "learn-teste-" + Guid.NewGuid().ToString("N")[..10]);
        var opcoes = Options.Create(new OpcoesDoVault { Raiz = _raizComum });
        _raiz = Path.Combine(_raizComum, Apelido);
        _repo = new RepositorioDeNotasEmDisco(
            opcoes,
            new RaizDoVaultDoUsuario(opcoes, new UsuarioDeTeste(Apelido)),
            NullLogger<RepositorioDeNotasEmDisco>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raizComum)) Directory.Delete(_raizComum, recursive: true);
    }

    [Fact]
    public async Task Grava_em_UTF8_SEM_BOM()
    {
        // Com BOM, o primeiro caractere de toda nota vira lixo invisível em metade das ferramentas — e o
        // vault precisa ser legível por qualquer uma delas, não só por esta aplicação.
        await _repo.GravarAsync(CaminhoNota.De("Acentuação.md"), "# Ação\n\nçãõ");

        var bytes = await File.ReadAllBytesAsync(Path.Combine(_raiz, "Acentuação.md"));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("# Ação\n\nçãõ", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Normaliza_a_quebra_de_linha_na_gravacao()
    {
        // Fecha o ciclo com a impressão digital: se o hash é sobre "\n" e o arquivo sai com "\r\n", a nota
        // recém-salva já nasce divergente do índice e a reconciliação a declara alterada para sempre.
        await _repo.GravarAsync(CaminhoNota.De("A.md"), "linha um\r\nlinha dois");

        var texto = await File.ReadAllTextAsync(Path.Combine(_raiz, "A.md"));
        Assert.DoesNotContain('\r', texto);

        var nota = await _repo.LerAsync(CaminhoNota.De("A.md"));
        Assert.Equal(ImpressaoDigital.De("linha um\nlinha dois"), nota!.Impressao);
    }

    [Fact]
    public async Task Cria_as_pastas_do_caminho()
    {
        await _repo.GravarAsync(CaminhoNota.De("Concursos/Direito/Licitações.md"), "conteúdo");
        Assert.True(File.Exists(Path.Combine(_raiz, "Concursos", "Direito", "Licitações.md")));
    }

    [Fact]
    public async Task Nao_deixa_temporario_para_tras()
    {
        // A gravação é atômica (temporário + troca). Se o temporário ficar, o vault do usuário enche de
        // lixo e o Obsidian mostra arquivos ".tmp-xxxx" na árvore dele.
        await _repo.GravarAsync(CaminhoNota.De("A.md"), "conteúdo");
        Assert.DoesNotContain(Directory.EnumerateFiles(_raiz), f => f.Contains(".tmp-"));
    }

    [Fact]
    public async Task Ler_devolve_nulo_quando_nao_existe()
    {
        Assert.Null(await _repo.LerAsync(CaminhoNota.De("NãoExiste.md")));
    }

    [Fact]
    public async Task Mover_recusa_destino_ocupado()
    {
        await _repo.GravarAsync(CaminhoNota.De("A.md"), "a");
        await _repo.GravarAsync(CaminhoNota.De("B.md"), "b");

        await Assert.ThrowsAsync<IOException>(() =>
            _repo.MoverAsync(CaminhoNota.De("A.md"), CaminhoNota.De("B.md")));
        Assert.Equal("b", await File.ReadAllTextAsync(Path.Combine(_raiz, "B.md")));
    }

    [Fact]
    public async Task Mover_limpa_a_pasta_que_ficou_vazia()
    {
        await _repo.GravarAsync(CaminhoNota.De("Antiga/A.md"), "a");
        await _repo.MoverAsync(CaminhoNota.De("Antiga/A.md"), CaminhoNota.De("Nova/A.md"));

        Assert.False(Directory.Exists(Path.Combine(_raiz, "Antiga")));
        Assert.True(File.Exists(Path.Combine(_raiz, "Nova", "A.md")));
    }

    [Fact]
    public async Task Varredura_pula_as_pastas_ignoradas()
    {
        // ".obsidian" é o caso que mais importa: o Obsidian salva o workspace o tempo todo, e indexar
        // aquilo dispararia reconciliação a cada tecla digitada lá dentro.
        await _repo.GravarAsync(CaminhoNota.De("Boa.md"), "conteúdo");
        Directory.CreateDirectory(Path.Combine(_raiz, ".obsidian"));
        await File.WriteAllTextAsync(Path.Combine(_raiz, ".obsidian", "workspace.md"), "config");
        Directory.CreateDirectory(Path.Combine(_raiz, ".git"));
        await File.WriteAllTextAsync(Path.Combine(_raiz, ".git", "COMMIT_EDITMSG.md"), "x");

        var achadas = new List<string>();
        await foreach (var e in _repo.VarrerAsync()) achadas.Add(e.Caminho.Valor);

        Assert.Equal(["Boa.md"], achadas);
    }

    [Fact]
    public async Task Varredura_ignora_arquivos_que_nao_sao_markdown()
    {
        await _repo.GravarAsync(CaminhoNota.De("Nota.md"), "conteúdo");
        await File.WriteAllTextAsync(Path.Combine(_raiz, "imagem.png"), "não é markdown");

        var achadas = new List<string>();
        await foreach (var e in _repo.VarrerAsync()) achadas.Add(e.Caminho.Valor);

        Assert.Equal(["Nota.md"], achadas);
    }

    [Fact]
    public async Task Varredura_devolve_a_impressao_do_conteudo()
    {
        await _repo.GravarAsync(CaminhoNota.De("A.md"), "conteúdo exato");
        var estado = await PrimeiroAsync();
        Assert.Equal(ImpressaoDigital.De("conteúdo exato"), estado.Impressao);
    }

    [Fact]
    public void Caminho_que_escapa_da_raiz_e_recusado_no_dominio()
    {
        // Defesa em profundidade: a barata é textual (aqui) e a cara é por caminho absoluto resolvido, no
        // adaptador. As duas juntas custam microssegundos e cobrem o link simbólico, que a textual não vê.
        Assert.False(CaminhoNota.TentarCriar("../../etc/passwd", out _, out _));
        Assert.False(CaminhoNota.TentarCriar("Direito/../../fora.md", out _, out _));
    }

    [Fact]
    public async Task Nota_atras_de_elo_simbolico_para_fora_do_vault_nao_e_lida()
    {
        // A metade "cara" da defesa em profundidade, e a que a conferência textual NÃO cobre: o caminho
        // "atalho/segredo.md" não tem "..", não é absoluto e resolve para dentro da raiz por
        // Path.GetFullPath. O que sai do vault é o destino do elo.
        var fora = Path.Combine(Path.GetTempPath(), "learn-fora-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(fora);
        await File.WriteAllTextAsync(Path.Combine(fora, "segredo.md"), "não deveria sair daqui");
        try
        {
            // A pasta do usuário é criada na primeira gravação; aqui o elo vem antes de qualquer nota.
            Directory.CreateDirectory(_raiz);
            Directory.CreateSymbolicLink(Path.Combine(_raiz, "atalho"), fora);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _repo.LerAsync(CaminhoNota.De("atalho/segredo.md")));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _repo.GravarAsync(CaminhoNota.De("atalho/invadida.md"), "conteúdo"));
        }
        finally
        {
            Directory.Delete(fora, recursive: true);
        }
    }

    [Fact]
    public async Task Recusa_nota_maior_que_o_limite()
    {
        var opcoes = Options.Create(new OpcoesDoVault { Raiz = _raizComum, TamanhoMaximoDaNotaBytes = 32 });
        var repoApertado = new RepositorioDeNotasEmDisco(
            opcoes,
            new RaizDoVaultDoUsuario(opcoes, new UsuarioDeTeste(Apelido)),
            NullLogger<RepositorioDeNotasEmDisco>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repoApertado.GravarAsync(CaminhoNota.De("Grande.md"), new string('x', 100)));
    }

    private async Task<Domain.Reconciliacao.EstadoDaNota> PrimeiroAsync()
    {
        await foreach (var e in _repo.VarrerAsync()) return e;
        throw new InvalidOperationException("A varredura não devolveu nada.");
    }
}
