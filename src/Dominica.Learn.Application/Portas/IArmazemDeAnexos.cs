using Dominica.Learn.Domain.Anexos;

namespace Dominica.Learn.Application.Portas;

/// <summary>
/// Onde ficam os ANEXOS. Hoje é a mesma pasta do vault; amanhã pode ser um bucket.
///
/// Separada de <see cref="IRepositorioDeNotas"/> mesmo compartilhando a raiz hoje, porque as duas
/// respondem a perguntas diferentes: aquela lida com TEXTO que é indexado, versionado e reconciliado;
/// esta com BYTES que só precisam ser guardados e devolvidos. Uma interface só teria métodos que metade
/// dos chamadores nunca usa — e é assim que porta vira balcão.
/// </summary>
public interface IArmazemDeAnexos
{
    Task<bool> ExisteAsync(CaminhoDeAnexo caminho, CancellationToken ct = default);

    /// <summary>Grava o conteúdo e devolve o caminho como ele ficou (pode diferir por colisão de nome).</summary>
    Task<CaminhoDeAnexo> GravarAsync(CaminhoDeAnexo caminho, Stream conteudo, CancellationToken ct = default);

    /// <summary>
    /// Abre o anexo para leitura. Null quando não existe.
    ///
    /// Devolve <see cref="Stream"/> e não byte[] de propósito: um PDF de 40 MB carregado em memória por
    /// requisição é o caminho mais curto para derrubar um VPS pequeno com três leitores simultâneos.
    /// </summary>
    Task<Stream?> AbrirAsync(CaminhoDeAnexo caminho, CancellationToken ct = default);

    /// <summary>
    /// Resolve a referência escrita numa nota (<c>![[foto.png]]</c>) para um anexo que existe.
    ///
    /// Tenta o caminho literal e depois a pasta padrão, que é a ordem do Obsidian: quem escreve o embed
    /// digita "foto.png", não "Anexos/foto.png", e a nota tem de continuar valendo se o arquivo for
    /// movido para a pasta de anexos depois.
    /// </summary>
    Task<CaminhoDeAnexo?> ResolverAsync(string referencia, CancellationToken ct = default);
}
