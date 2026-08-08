using System.Text;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Cartoes;

/// <summary>
/// EXPORTA OS CARTÕES no formato de importação em texto do Anki.
///
/// É O ARQUIVO DE TEXTO, e não um .apkg, de propósito: o .apkg é um zip com um banco SQLite dentro, e
/// gerá-lo exigiria reproduzir o esquema interno do Anki — que muda entre versões e falha em silêncio
/// quando diverge. O texto separado por tabulação é o formato de importação DOCUMENTADO do Anki
/// (Arquivo → Importar), com cabeçalhos que dizem ao importador o que cada coluna é. Menos mágico, e
/// funciona em qualquer versão.
///
/// O QUE VAI: frente, verso e a matéria como tag (com "dominica" junto, para o lote inteiro poder ser
/// achado e removido no Anki). O AGENDAMENTO NÃO VAI — o formato de texto não carrega agendamento, e
/// está certo assim: quem muda de ferramenta recomeça o ritmo nela; os arquivos .md continuam sendo a
/// origem da verdade aqui.
///
/// SUSPENSO VAI JUNTO, marcado com a tag "suspenso": exportar é levar o baralho, e o suspenso faz parte
/// dele — quem não o quiser filtra pela tag no Anki, que é o lugar da decisão nova.
/// </summary>
public static class ExportadorDeAnki
{
    public static string Exportar(IReadOnlyList<Cartao> cartoes)
    {
        ArgumentNullException.ThrowIfNull(cartoes);

        var sb = new StringBuilder();
        // Os cabeçalhos "#" são lidos pelo importador do Anki (2.1.54+) e ignorados como comentário
        // pelos antigos — nos antigos a pessoa aponta as colunas à mão, e o arquivo continua válido.
        sb.Append("#separator:tab\n");
        sb.Append("#html:true\n");
        sb.Append("#tags column:3\n");

        foreach (var c in cartoes)
        {
            var tags = new List<string> { "dominica" };
            var materia = Materia.De(c.Nota);
            if (materia.Existe) tags.Add(TagDoAnki(materia.Nome));
            if (c.Suspenso) tags.Add("suspenso");

            sb.Append(Campo(c.Frente)).Append('\t')
              .Append(Campo(c.Verso)).Append('\t')
              .Append(string.Join(' ', tags)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Um campo do Anki: HTML escapado (o cartão é texto, não marcação) e quebras viram &lt;br&gt; —
    /// no modo #html:true é a única forma de um verso de várias linhas sobreviver ao formato tabular.
    /// </summary>
    private static string Campo(string texto) => (texto ?? string.Empty)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        // O "\r" SOZINHO também tem de sair: o importador do Anki quebra registro em "\r" tanto quanto
        // em "\n", então um return solto (nota vinda de arquivo de Mac antigo) partiria a linha em duas
        // e deslocaria o baralho inteiro dali para baixo.
        .Replace("\r", "\n", StringComparison.Ordinal)
        .Replace("\t", " ", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal);

    /// <summary>Tag do Anki não tem espaço — "Direito Administrativo" vira "Direito-Administrativo".</summary>
    private static string TagDoAnki(string nome) =>
        string.Join('-', nome.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
