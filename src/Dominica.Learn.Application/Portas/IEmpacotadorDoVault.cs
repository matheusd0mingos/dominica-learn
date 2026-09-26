using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>O que um import fez, item a item. É isto que a tela mostra — nada é descartado em silêncio.</summary>
public sealed record ResultadoDaImportacao(
    int Aceitas,
    IReadOnlyList<EntradaJulgada> Recusadas,
    string PastaDeChegada);

/// <summary>
/// Empacota o vault para baixar, e desempacota um pacote de volta.
///
/// POR QUE ISTO É UMA PORTA, e não um `ZipArchive` no meio de um caso de uso: o formato do pacote é
/// detalhe de infraestrutura. Hoje é .zip porque é o que qualquer sistema operacional abre com dois
/// cliques; amanhã pode ser .tar.gz num servidor, ou um envio direto para um bucket. Nada na aplicação
/// deveria precisar saber.
///
/// O QUE ESTA PORTA GARANTE, e é a razão de ela existir:
///
///   EXPORTAR é a prova da promessa central do produto. "O vault é seu" só é verificável se você
///   conseguir levá-lo embora sem pedir nada a ninguém — sem acesso ao servidor, sem SQL, sem suporte.
///   Um botão que baixa tudo é o que transforma a promessa de discurso em fato.
///
///   IMPORTAR nunca mescla na raiz. Ver <see cref="RegrasDeImportacao"/>: o pacote aterrissa numa
///   pasta carimbada com a data, e por isso não existe sobrescrita possível.
/// </summary>
public interface IEmpacotadorDoVault
{
    /// <summary>
    /// Escreve o vault inteiro do usuário atual em <paramref name="destino"/>, como .zip.
    ///
    /// Recebe o fluxo de saída em vez de devolver um — um vault de anos não deve passar pela memória
    /// do servidor para virar um array de bytes antes de sair pela rede.
    /// </summary>
    Task ExportarAsync(Stream destino, CancellationToken ct = default);

    /// <summary>
    /// Lê um pacote e grava no vault do usuário atual o que as regras aceitarem.
    /// </summary>
    Task<ResultadoDaImportacao> ImportarAsync(Stream pacote, CancellationToken ct = default);
}
