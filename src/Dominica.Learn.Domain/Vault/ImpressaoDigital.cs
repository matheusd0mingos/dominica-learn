using System.Security.Cryptography;
using System.Text;

namespace Dominica.Learn.Domain.Vault;

/// <summary>
/// Impressão digital do CONTEÚDO de uma nota (SHA-256, em hexadecimal minúsculo).
///
/// POR QUE EXISTE: o vault abre no Obsidian, então os arquivos mudam pelas costas da aplicação. Para saber
/// o que mudou não dá para confiar só na data de modificação — ela é reescrita por sincronizador de nuvem,
/// por `git checkout`, por cópia de backup, e tem resolução grosseira em alguns sistemas de arquivos. Uma
/// nota "modificada" cuja impressão não mudou é ruído; uma nota com data antiga e impressão diferente é
/// mudança de verdade. A impressão é o que distingue os dois casos.
///
/// É também o que permite detectar RENOMEAÇÃO: sumiu um caminho, apareceu outro, mesma impressão — é a
/// mesma nota mudando de lugar, e o histórico dela deve seguir junto em vez de virar "apagada + criada".
/// </summary>
public readonly record struct ImpressaoDigital
{
    /// <summary>Hexadecimal minúsculo de 64 caracteres.</summary>
    public string Valor { get; }

    private ImpressaoDigital(string valor) => Valor = valor;

    /// <summary>
    /// Calcula sobre os BYTES UTF-8 do texto, com as quebras de linha normalizadas para "\n".
    ///
    /// A normalização não é capricho: Windows grava CRLF, Linux e macOS gravam LF, e o mesmo vault
    /// sincronizado entre as duas máquinas produziria impressões diferentes para um texto idêntico —
    /// o que faria o reconciliador declarar "todas as notas mudaram" a cada troca de computador.
    /// </summary>
    public static ImpressaoDigital De(string conteudo)
    {
        var normalizado = NormalizarQuebras(conteudo ?? string.Empty);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizado));
        return new ImpressaoDigital(Convert.ToHexStringLower(bytes));
    }

    /// <summary>Reidrata uma impressão já calculada (vinda do índice). Não recalcula.</summary>
    public static ImpressaoDigital Reidratar(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 64)
            throw new ArgumentException("Impressão digital deve ter 64 caracteres hexadecimais.", nameof(hex));
        return new ImpressaoDigital(hex.ToLowerInvariant());
    }

    /// <summary>
    /// A mesma normalização usada no cálculo, exposta para quem GRAVA no disco.
    ///
    /// Existe para fechar um ciclo que sem ela fica aberto: se a impressão é calculada sobre "\n" mas o
    /// arquivo é gravado com "\r\n", a nota recém-salva já nasce com impressão diferente da que está no
    /// disco — e a reconciliação seguinte a declararia alterada, para sempre, a cada salvamento.
    /// </summary>
    public static string NormalizarParaGravacao(string conteudo) => NormalizarQuebras(conteudo ?? string.Empty);

    internal static string NormalizarQuebras(string texto) =>
        texto.Contains('\r') ? texto.Replace("\r\n", "\n").Replace('\r', '\n') : texto;

    public override string ToString() => Valor;
}
