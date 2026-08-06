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
public sealed class ArmazemDeAnexosEmDisco(RaizDoVaultDoUsuario raizDoUsuario, ILogger<ArmazemDeAnexosEmDisco> log)
    : IArmazemDeAnexos
{

    public async Task<bool> ExisteAsync(CaminhoDeAnexo caminho, CancellationToken ct = default) =>
        File.Exists(Absoluto(await raizDoUsuario.ObterAsync(ct), caminho));

    public async Task<CaminhoDeAnexo> GravarAsync(CaminhoDeAnexo caminho, Stream conteudo, CancellationToken ct = default)
    {
        var absoluto = Absoluto(await raizDoUsuario.ObterAsync(ct), caminho);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluto)!);

        // CreateNew, e não Create: é isto que fecha a janela entre "procurei um nome livre" e "gravei".
        // Dois uploads simultâneos do mesmo nome — o segundo falha em vez de apagar o primeiro.
        await using (var destino = new FileStream(absoluto, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await conteudo.CopyToAsync(destino, ct);
        }

        log.LogInformation("Anexo gravado em {Caminho}.", caminho);
        return caminho;
    }

    public async Task<Stream?> AbrirAsync(CaminhoDeAnexo caminho, CancellationToken ct = default)
    {
        var absoluto = Absoluto(await raizDoUsuario.ObterAsync(ct), caminho);
        if (!File.Exists(absoluto)) return null;

        // FileShare.Read: o Obsidian ou um sincronizador pode estar lendo o mesmo arquivo, e travá-lo
        // durante o download faria a imagem sumir da tela sem motivo aparente.
        return new FileStream(absoluto, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
    }

    public async Task<CaminhoDeAnexo?> ResolverAsync(string referencia, CancellationToken ct = default)
    {
        var raiz = await raizDoUsuario.ObterAsync(ct);

        // 1) o caminho como está escrito
        if (CaminhoDeAnexo.TentarCriar(referencia, out var literal, out _) && literal is not null
            && File.Exists(Absoluto(raiz, literal)))
            return literal;

        // 2) o mesmo nome dentro da pasta de anexos — a ordem do Obsidian: quem escreve o embed digita
        //    "foto.png", não "Anexos/foto.png".
        var soNome = referencia.Replace('\\', '/');
        soNome = soNome[(soNome.LastIndexOf('/') + 1)..];
        if (CaminhoDeAnexo.TentarCriar($"{CaminhoDeAnexo.PastaPadrao}/{soNome}", out var naPasta, out _) && naPasta is not null
            && File.Exists(Absoluto(raiz, naPasta)))
            return naPasta;

        // Deliberadamente NÃO há passo 3 varrendo o vault inteiro atrás do nome. Seria conveniente e
        // custaria uma varredura de disco por imagem exibida — numa nota com vinte figuras, vinte
        // varreduras por abertura.
        return null;
    }

    /// <summary>Caminho absoluto, com a garantia de que não escapou da raiz — nem por elo simbólico.</summary>
    private static string Absoluto(string raiz, CaminhoDeAnexo caminho) =>
        CaminhoSeguro.Combinar(raiz, caminho.Valor, caminho.Valor);
}
