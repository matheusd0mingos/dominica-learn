using System.Runtime.CompilerServices;
using System.Text;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Infrastructure.Vault;

/// <summary>
/// O adaptador do VAULT: uma pasta de arquivos .md. É a fonte da verdade do produto.
///
/// Duas regras de higiene que este adaptador não pode relaxar:
///
/// 1. TUDO É UTF-8 SEM BOM, COM QUEBRA "\n". É o que o Obsidian escreve e o que o Git entende sem
///    reclamar. Gravar com BOM faria o primeiro caractere de toda nota virar lixo invisível em metade
///    das ferramentas; gravar CRLF encheria os diffs de linhas fantasma.
///
/// 2. NENHUM CAMINHO SAI DA RAIZ. O <see cref="CaminhoNota"/> já recusa ".." — aqui a conferência é
///    feita de novo, e sobre o caminho com os ELOS SIMBÓLICOS RESOLVIDOS (ver <see cref="CaminhoSeguro"/>),
///    porque um elo dentro do vault produz um caminho que começa na raiz e ainda assim lê fora dela.
///    Defesa em profundidade: a barata é textual, a cara é esta, e as duas juntas custam microssegundos.
/// </summary>
public sealed class RepositorioDeNotasEmDisco : IRepositorioDeNotas
{
    private static readonly UTF8Encoding Utf8SemBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly OpcoesDoVault _opcoes;
    private readonly ILogger<RepositorioDeNotasEmDisco> _log;
    private readonly RaizDoVaultDoUsuario _raizDoUsuario;

    public RepositorioDeNotasEmDisco(
        IOptions<OpcoesDoVault> opcoes, RaizDoVaultDoUsuario raizDoUsuario, ILogger<RepositorioDeNotasEmDisco> log)
    {
        _opcoes = opcoes.Value;
        _raizDoUsuario = raizDoUsuario;
        _log = log;
    }

    public async Task<bool> ExisteAsync(CaminhoNota caminho, CancellationToken ct = default) =>
        File.Exists(Absoluto(await _raizDoUsuario.ObterAsync(ct), caminho));

    public async Task<Nota?> LerAsync(CaminhoNota caminho, CancellationToken ct = default)
    {
        var absoluto = Absoluto(await _raizDoUsuario.ObterAsync(ct), caminho);
        if (!File.Exists(absoluto)) return null;

        try
        {
            var conteudo = await File.ReadAllTextAsync(absoluto, ct);
            return Nota.Criar(caminho, conteudo, new DateTimeOffset(File.GetLastWriteTimeUtc(absoluto), TimeSpan.Zero));
        }
        catch (IOException e)
        {
            // O Obsidian pode estar gravando o mesmo arquivo neste instante. Não é defeito: é o produto
            // funcionando como prometido. Quem chamou trata como "sumiu"; a próxima reconciliação pega.
            _log.LogDebug(e, "Nota {Caminho} indisponível no momento da leitura.", caminho);
            return null;
        }
    }

    public async Task<Nota> GravarAsync(CaminhoNota caminho, string conteudo, CancellationToken ct = default)
    {
        var texto = ImpressaoDigital.NormalizarParaGravacao(conteudo);
        var bytes = Utf8SemBom.GetByteCount(texto);
        if (bytes > _opcoes.TamanhoMaximoDaNotaBytes)
            throw new InvalidOperationException(
                $"A nota tem {bytes} bytes e o limite é {_opcoes.TamanhoMaximoDaNotaBytes}. Ajuste Vault:TamanhoMaximoDaNotaBytes se for intencional.");

        var absoluto = Absoluto(await _raizDoUsuario.ObterAsync(ct), caminho);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluto)!);

        // GRAVAÇÃO ATÔMICA: escreve num temporário ao lado e troca. Sem isso, uma queda de energia no meio
        // de um autosave deixaria a nota truncada — e nota de estudo truncada é conhecimento perdido, não
        // um registro que dá para reprocessar. O temporário fica na MESMA pasta de propósito: File.Move
        // entre sistemas de arquivos diferentes não é atômico.
        var temporario = absoluto + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await File.WriteAllTextAsync(temporario, texto, Utf8SemBom, ct);
            File.Move(temporario, absoluto, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporario)) TentarApagar(temporario);
            throw;
        }

        return Nota.Criar(caminho, texto, new DateTimeOffset(File.GetLastWriteTimeUtc(absoluto), TimeSpan.Zero));
    }

    public async Task MoverAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
    {
        var raiz = await _raizDoUsuario.ObterAsync(ct);
        var origem = Absoluto(raiz, de);
        var destino = Absoluto(raiz, para);
        if (!File.Exists(origem)) throw new FileNotFoundException($"Nota não encontrada: {de}", origem);
        // overwrite: false de propósito — sobrescrever nota alheia em silêncio é perda de dados, e o caso
        // de uso já checou a existência antes de chegar aqui.
        if (File.Exists(destino)) throw new IOException($"Já existe uma nota em {para}.");

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.Move(origem, destino);
        LimparPastaVaziaAcimaDe(raiz, origem);
    }

    public async Task ApagarAsync(CaminhoNota caminho, CancellationToken ct = default)
    {
        var raiz = await _raizDoUsuario.ObterAsync(ct);
        var absoluto = Absoluto(raiz, caminho);
        if (File.Exists(absoluto))
        {
            File.Delete(absoluto);
            LimparPastaVaziaAcimaDe(raiz, absoluto);
        }
    }

    public async IAsyncEnumerable<EstadoDaNota> VarrerAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var ignoradas = _opcoes.PastasIgnoradas.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var raiz = await _raizDoUsuario.ObterAsync(ct);

        foreach (var absoluto in EnumerarArquivos(raiz, ignoradas))
        {
            ct.ThrowIfCancellationRequested();

            var relativo = Path.GetRelativePath(raiz, absoluto).Replace(Path.DirectorySeparatorChar, '/');
            if (!CaminhoNota.TentarCriar(relativo, out var caminho, out var erro) || caminho is null)
            {
                // Arquivo com nome que o domínio recusa (caractere ilegal para outro sistema, por exemplo).
                // Ignorar em silêncio esconderia uma nota do usuário — o aviso é o mínimo devido.
                _log.LogWarning("Arquivo ignorado na varredura ({Motivo}): {Arquivo}", erro, relativo);
                continue;
            }

            string conteudo;
            DateTimeOffset modificado;
            try
            {
                conteudo = await File.ReadAllTextAsync(absoluto, ct);
                modificado = new DateTimeOffset(File.GetLastWriteTimeUtc(absoluto), TimeSpan.Zero);
            }
            catch (IOException e)
            {
                _log.LogDebug(e, "Arquivo ocupado durante a varredura: {Arquivo}", relativo);
                continue;
            }

            yield return new EstadoDaNota(caminho, modificado, ImpressaoDigital.De(conteudo));
        }
    }

    /// <summary>
    /// Enumera os .md pulando as pastas ignoradas.
    ///
    /// A recursão é manual, e não EnumerateFiles com SearchOption.AllDirectories, porque aquele desce em
    /// TUDO — inclusive em .git e .obsidian — e só depois deixaria filtrar. Num vault versionado, isso é
    /// a diferença entre varrer centenas de arquivos e varrer dezenas de milhares.
    /// </summary>
    private static IEnumerable<string> EnumerarArquivos(string pasta, HashSet<string> ignoradas)
    {
        IEnumerable<string> arquivos;
        IEnumerable<string> subpastas;
        try
        {
            arquivos = Directory.EnumerateFiles(pasta, "*" + CaminhoNota.Extensao);
            subpastas = Directory.EnumerateDirectories(pasta);
        }
        catch (UnauthorizedAccessException) { yield break; }
        catch (DirectoryNotFoundException) { yield break; }

        foreach (var a in arquivos) yield return a;

        foreach (var sub in subpastas)
        {
            var nome = Path.GetFileName(sub);
            if (ignoradas.Contains(nome)) continue;
            foreach (var a in EnumerarArquivos(sub, ignoradas)) yield return a;
        }
    }

    /// <summary>Caminho absoluto, com a garantia de que não escapou da raiz — nem por elo simbólico.</summary>
    private static string Absoluto(string raiz, CaminhoNota caminho) =>
        CaminhoSeguro.Combinar(raiz, caminho.Valor, caminho.Valor);

    /// <summary>
    /// Depois de mover/apagar, remove as pastas que ficaram vazias — até a raiz, sem incluí-la.
    /// Pasta vazia na árvore do vault é ruído visual que ninguém apaga na mão e que o Obsidian também
    /// esconde. Falha aqui é irrelevante: pasta vazia não perde dado de ninguém.
    /// </summary>
    private void LimparPastaVaziaAcimaDe(string raiz, string arquivoAbsoluto)
    {
        var pasta = Path.GetDirectoryName(arquivoAbsoluto);
        while (!string.IsNullOrEmpty(pasta) && !PathEhRaiz(raiz, pasta))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(pasta).Any()) return;
                Directory.Delete(pasta);
                pasta = Path.GetDirectoryName(pasta);
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
        }
    }

    // A raiz aqui é a do USUÁRIO, e é o que impede a limpeza de pasta vazia de subir e apagar a pasta
    // do vault dele — ou, pior, a raiz comum onde moram os vaults de todo mundo.
    private static bool PathEhRaiz(string raiz, string pasta) =>
        string.Equals(Path.GetFullPath(pasta).TrimEnd(Path.DirectorySeparatorChar),
                      raiz.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);

    private void TentarApagar(string arquivo)
    {
        try { File.Delete(arquivo); }
        catch (IOException e) { _log.LogDebug(e, "Temporário não pôde ser removido: {Arquivo}", arquivo); }
    }
}
