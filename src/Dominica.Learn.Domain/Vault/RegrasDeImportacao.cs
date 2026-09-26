using Dominica.Learn.Domain.Anexos;

namespace Dominica.Learn.Domain.Vault;

/// <summary>Por que uma entrada do pacote foi recusada. O usuário merece saber, item a item.</summary>
public enum MotivoDaRecusa
{
    /// <summary>Caminho que tenta sair da pasta de destino — o "zip slip".</summary>
    CaminhoEscapa,
    /// <summary>Tipo de arquivo fora da lista de permissão.</summary>
    TipoNaoPermitido,
    /// <summary>O pacote inteiro passou do tamanho ou da contagem máxima.</summary>
    PacoteGrandeDemais,
    /// <summary>Entrada de tamanho absurdo para um arquivo de texto — sinal de bomba.</summary>
    ArquivoGrandeDemais,
    /// <summary>Nome vazio, só pasta, ou impossível de virar caminho de nota.</summary>
    NomeInvalido,
}

/// <summary>Uma entrada do pacote, já julgada.</summary>
public sealed record EntradaJulgada(string NomeNoPacote, string? Destino, MotivoDaRecusa? Recusa)
{
    public bool Aceita => Recusa is null && Destino is not null;
}

/// <summary>
/// Decide, ENTRADA POR ENTRADA, o que pode sair de um pacote .zip e ir para o vault.
///
/// POR QUE ISTO É DOMÍNIO, e não um detalhe do descompactador: um zip enviado por usuário é uma das
/// entradas mais hostis que um sistema aceita, e as defesas são REGRAS — não truques de biblioteca.
/// Deixá-las na infraestrutura tiraria do teste exatamente a parte que precisa ser testada, e
/// espalharia por um laço de descompactação decisões que precisam estar num lugar só.
///
/// AS QUATRO AMEAÇAS, e por que cada defesa é como é:
///
///   ZIP SLIP — uma entrada chamada "../../etc/algo" escreve FORA da pasta de destino. É a mais
///   antiga e a mais explorada. A defesa não é procurar ".." no texto (dá para escondê-lo com
///   codificação e com barra invertida): é MONTAR o caminho pelas regras do CaminhoNota, que só
///   aceita caminho relativo normalizado, e recusar o que não passar.
///
///   ZIP BOMB — um megabyte que descompacta para quarenta gigabytes e enche o disco do servidor,
///   derrubando o vault de TODO MUNDO. A defesa é um teto no total DESCOMPACTADO e na contagem de
///   entradas, decidido antes de gravar qualquer coisa.
///
///   TIPO — o vault aceita .md e a lista de anexos. SVG está de fora porque é XML que executa script,
///   e um vault é renderizado no navegador de quem o abre.
///
///   NADA SOBRESCREVE — e esta é a decisão de produto, não de segurança: o import cai sempre numa
///   PASTA CARIMBADA com a data. Sem colisão não há sobrescrita, e o que chegou fica visível e
///   separado até a pessoa decidir o que fazer. Mesclar um pacote de outra pessoa direto na raiz do
///   vault é a operação que ninguém consegue desfazer.
/// </summary>
public static class RegrasDeImportacao
{
    /// <summary>Teto do pacote descompactado. Um vault de texto de anos não chega perto disto.</summary>
    public const long TamanhoMaximoDescompactadoBytes = 512L * 1024 * 1024;

    /// <summary>Teto de entradas. Vault real de uma pessoa raramente passa de alguns milhares.</summary>
    public const int MaximoDeEntradas = 20_000;

    /// <summary>Teto por arquivo. Uma nota .md com mais que isto não é uma nota.</summary>
    public const long TamanhoMaximoDeNotaBytes = 8L * 1024 * 1024;

    /// <summary>O nome da pasta em que o pacote aterrissa. Ver o comentário da classe.</summary>
    public static string PastaDeChegada(DateOnly dia) => $"Importado {dia:yyyy-MM-dd}";

    /// <summary>
    /// Julga o pacote inteiro. <paramref name="entradas"/> é (nome dentro do zip, tamanho
    /// descompactado). Devolve na mesma ordem, cada uma com o destino ou o motivo da recusa.
    /// </summary>
    public static IReadOnlyList<EntradaJulgada> Julgar(
        IEnumerable<(string Nome, long Bytes)> entradas, string pastaDeChegada)
    {
        ArgumentNullException.ThrowIfNull(entradas);

        var julgadas = new List<EntradaJulgada>();
        long acumulado = 0;
        var contadas = 0;

        foreach (var (nome, bytes) in entradas)
        {
            // Diretórios vêm como entradas terminadas em barra; não são arquivo e não são erro.
            if (string.IsNullOrWhiteSpace(nome) || nome.EndsWith('/') || nome.EndsWith('\\'))
            {
                julgadas.Add(new EntradaJulgada(nome, null, MotivoDaRecusa.NomeInvalido));
                continue;
            }

            contadas++;
            if (contadas > MaximoDeEntradas)
            {
                julgadas.Add(new EntradaJulgada(nome, null, MotivoDaRecusa.PacoteGrandeDemais));
                continue;
            }

            if (bytes > TamanhoMaximoDeNotaBytes && EhNota(nome))
            {
                julgadas.Add(new EntradaJulgada(nome, null, MotivoDaRecusa.ArquivoGrandeDemais));
                continue;
            }

            // O acumulado é conferido ANTES de aceitar: aceitar e depois estourar significaria já ter
            // gravado metade da bomba no disco.
            if (acumulado + bytes > TamanhoMaximoDescompactadoBytes)
            {
                julgadas.Add(new EntradaJulgada(nome, null, MotivoDaRecusa.PacoteGrandeDemais));
                continue;
            }

            var julgada = Destino(nome, pastaDeChegada);
            if (julgada.Aceita) acumulado += bytes;
            julgadas.Add(julgada);
        }

        return julgadas;
    }

    private static EntradaJulgada Destino(string nome, string pastaDeChegada)
    {
        // Barra invertida do Windows vira barra normal ANTES de qualquer julgamento: "..\\..\\x" é a
        // mesma ameaça que "../../x", e olhar só para uma das duas grafias é olhar para metade.
        var limpo = nome.Replace('\\', '/').Trim();

        // Um caminho ABSOLUTO no pacote ("/etc/passwd", "C:/x") não é ambíguo: é tentativa de escapar.
        if (limpo.StartsWith('/') || (limpo.Length > 1 && limpo[1] == ':'))
            return new EntradaJulgada(nome, null, MotivoDaRecusa.CaminhoEscapa);

        // A defesa contra zip slip é MONTAR pelas regras do CaminhoNota — que recusa "..", caminho
        // absoluto e barra invertida — e não procurar ".." no texto, que se esconde de mil formas.
        var candidato = $"{pastaDeChegada}/{limpo}";

        if (EhNota(limpo))
        {
            return CaminhoNota.TentarCriar(candidato, out var caminho, out _) && caminho is not null
                ? new EntradaJulgada(nome, caminho.Valor, null)
                : new EntradaJulgada(nome, null, MotivoDaRecusa.CaminhoEscapa);
        }

        // Não é nota: só passa se for um tipo de anexo permitido. A lista exclui SVG de propósito —
        // é XML que executa script, e o vault é renderizado no navegador de quem o abre.
        if (!TiposDeAnexo.EhPermitida(Path.GetExtension(limpo)))
            return new EntradaJulgada(nome, null, MotivoDaRecusa.TipoNaoPermitido);

        return CaminhoDeAnexo.TentarCriar(candidato, out var anexo, out _) && anexo is not null
            ? new EntradaJulgada(nome, anexo.Valor, null)
            : new EntradaJulgada(nome, null, MotivoDaRecusa.CaminhoEscapa);
    }

    private static bool EhNota(string nome) =>
        nome.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
}
