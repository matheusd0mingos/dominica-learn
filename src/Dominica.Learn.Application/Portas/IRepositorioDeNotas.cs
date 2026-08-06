using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>
/// O VAULT — a fonte da verdade. Hoje é uma pasta de arquivos .md; amanhã pode ser S3, WebDAV ou um
/// repositório git. Nada nesta interface revela qual.
///
/// Repare no que ela NÃO tem: nada de "commit", "transação" ou "rollback". Um sistema de arquivos não
/// oferece isso, e prometer aqui uma garantia que o adaptador não consegue cumprir seria mentir para
/// todos os casos de uso de uma vez.
/// </summary>
public interface IRepositorioDeNotas
{
    Task<bool> ExisteAsync(CaminhoNota caminho, CancellationToken ct = default);

    /// <summary>Lê a nota. Null quando não existe — ausência não é excepcional num vault que muda por fora.</summary>
    Task<Nota?> LerAsync(CaminhoNota caminho, CancellationToken ct = default);

    /// <summary>Grava (criando ou sobrescrevendo) e devolve a nota como ficou no disco.</summary>
    Task<Nota> GravarAsync(CaminhoNota caminho, string conteudo, CancellationToken ct = default);

    /// <summary>Move/renomeia. Falha se o destino já existir — sobrescrever nota alheia em silêncio é perda de dados.</summary>
    Task MoverAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default);

    Task ApagarAsync(CaminhoNota caminho, CancellationToken ct = default);

    /// <summary>
    /// Varre o vault inteiro devolvendo só metadados (caminho, data, impressão) — sem carregar conteúdo.
    /// É o lado "disco" da reconciliação; carregar dez mil notas inteiras para comparar hashes seria
    /// gastar gigabytes para responder uma pergunta de kilobytes.
    /// </summary>
    IAsyncEnumerable<EstadoDaNota> VarrerAsync(CancellationToken ct = default);
}
