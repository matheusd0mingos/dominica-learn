using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Anexos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Infrastructure.Vault;

/// <summary>
/// Anexos numa pasta do vault — a MESMA raiz das notas, para que o Obsidian encontre a imagem que o
/// <c>![[…]]</c> referencia.
///
/// As duas regras que este adaptador não pode relaxar são as mesmas do repositório de notas, e valem
/// ainda mais aqui: o caminho de um anexo vem de um <c>![[…]]</c> digitado dentro de uma nota, portanto é
/// texto arbitrário chegando a uma chamada de sistema de arquivos. Conferir o caminho RESOLVIDO contra a
/// raiz é o que barra o link simbólico que nenhuma validação textual pega.
/// </summary>
public sealed class ArmazemDeAnexosEmDisco : IArmazemDeAnexos
{
    private readonly ILogger<ArmazemDeAnexosEmDisco> _log;
    private readonly string _raiz;

    public ArmazemDeAnexosEmDisco(IOptions<OpcoesDoVault> opcoes, ILogger<ArmazemDeAnexosEmDisco> log)
    {
        _log = log;
        Directory.CreateDirectory(opcoes.Value.Raiz);
        // A raiz é resolvida UMA vez, com elos e tudo: comparar caminho resolvido com raiz não resolvida
        // recusaria acesso legítimo sempre que a própria raiz estiver atrás de um elo simbólico.
        _raiz = CaminhoSeguro.Real(opcoes.Value.Raiz);
    }

    public Task<bool> ExisteAsync(CaminhoDeAnexo caminho, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(Absoluto(caminho)));

    public async Task<CaminhoDeAnexo> GravarAsync(CaminhoDeAnexo caminho, Stream conteudo, CancellationToken ct = default)
    {
        var absoluto = Absoluto(caminho);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluto)!);

        // CreateNew, e não Create: é isto que fecha a janela entre "procurei um nome livre" e "gravei".
        // Dois uploads simultâneos do mesmo nome — o segundo falha em vez de apagar o primeiro.
        await using (var destino = new FileStream(absoluto, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await conteudo.CopyToAsync(destino, ct);
        }

        _log.LogInformation("Anexo gravado em {Caminho}.", caminho);
        return caminho;
    }

    public Task<Stream?> AbrirAsync(CaminhoDeAnexo caminho, CancellationToken ct = default)
    {
        var absoluto = Absoluto(caminho);
        if (!File.Exists(absoluto)) return Task.FromResult<Stream?>(null);

        // FileShare.Read: o Obsidian ou um sincronizador pode estar lendo o mesmo arquivo, e travá-lo
        // durante o download faria a imagem sumir da tela sem motivo aparente.
        Stream fluxo = new FileStream(absoluto, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
        return Task.FromResult<Stream?>(fluxo);
    }

    public Task<CaminhoDeAnexo?> ResolverAsync(string referencia, CancellationToken ct = default)
    {
        // 1) o caminho como está escrito
        if (CaminhoDeAnexo.TentarCriar(referencia, out var literal, out _) && literal is not null
            && File.Exists(Absoluto(literal)))
            return Task.FromResult<CaminhoDeAnexo?>(literal);

        // 2) o mesmo nome dentro da pasta de anexos — a ordem do Obsidian: quem escreve o embed digita
        //    "foto.png", não "Anexos/foto.png".
        var soNome = referencia.Replace('\\', '/');
        soNome = soNome[(soNome.LastIndexOf('/') + 1)..];
        if (CaminhoDeAnexo.TentarCriar($"{CaminhoDeAnexo.PastaPadrao}/{soNome}", out var naPasta, out _) && naPasta is not null
            && File.Exists(Absoluto(naPasta)))
            return Task.FromResult<CaminhoDeAnexo?>(naPasta);

        // Deliberadamente NÃO há passo 3 varrendo o vault inteiro atrás do nome. Seria conveniente e
        // custaria uma varredura de disco por imagem exibida — numa nota com vinte figuras, vinte
        // varreduras por abertura.
        return Task.FromResult<CaminhoDeAnexo?>(null);
    }

    /// <summary>Caminho absoluto, com a garantia de que não escapou da raiz — nem por elo simbólico.</summary>
    private string Absoluto(CaminhoDeAnexo caminho) => CaminhoSeguro.Combinar(_raiz, caminho.Valor, caminho.Valor);
}
