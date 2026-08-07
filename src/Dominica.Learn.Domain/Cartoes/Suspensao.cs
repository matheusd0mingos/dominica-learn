namespace Dominica.Learn.Domain.Cartoes;

/// <summary>
/// Tirar um cartão da fila sem apagá-lo.
///
/// POR QUE ISTO PRECISA EXISTIR: um cartão mal formulado só sai apagando, e apagar é irreversível
/// justamente quando a pessoa ainda não decidiu. O que acontece na prática é pior que qualquer um dos
/// dois: ela responde "bom" num cartão que não presta, só para ele sair da frente — e o cartão volta
/// daqui a três dias, e a fila vira um lugar onde se mente para o algoritmo.
///
/// A MARCA É UM COMENTÁRIO HTML PRÓPRIO, ao lado do agendamento e não dentro dele:
///
///     Prazo::3 dias &lt;!--SR:!2026-09-01,12,250--&gt;&lt;!--suspenso--&gt;
///
///   • o agendamento fica INTACTO — dessuspender devolve o cartão com o histórico que ele tinha, e não
///     como se fosse novo. É a diferença entre "guardar" e "recomeçar";
///
///   • o Obsidian não mostra comentário HTML, então o texto de quem estuda não muda;
///
///   • não se estica o formato do plugin. Datar o cartão para o ano 9999 seria o truque óbvio e uma
///     mentira gravada no arquivo: qualquer leitor — inclusive este código daqui a cinco anos — leria
///     "vence em 9999" como agendamento de verdade.
///
/// A DIVERGÊNCIA, dita às claras: o plugin do Obsidian não tem suspensão e vai continuar mostrando o
/// cartão lá. Não há o que sincronizar — não existe o conceito do outro lado — e a alternativa seria
/// esconder o cartão de um jeito que corrompesse a leitura dele no Obsidian, que é pior.
/// </summary>
public static class Suspensao
{
    public const string Marca = "<!--suspenso-->";

    public static bool Tem(string? texto) =>
        texto is not null && texto.Contains(Marca, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Suspende ou dessuspende o cartão da linha indicada. Devolve a MESMA instância quando não há o que
    /// mudar — pelo mesmo motivo de <see cref="EscritorDeAgendamento"/>: gravar um arquivo idêntico
    /// acordaria o vigia do vault sem necessidade.
    /// </summary>
    public static string Definir(string conteudo, int linha, bool suspenso)
    {
        if (string.IsNullOrEmpty(conteudo)) return conteudo;

        var crlf = conteudo.Contains("\r\n", StringComparison.Ordinal);
        var linhas = conteudo.Replace("\r\n", "\n").Split('\n');
        if (linha < 0 || linha >= linhas.Length) return conteudo;

        var atual = linhas[linha];
        var jaTem = Tem(atual);
        if (jaTem == suspenso) return conteudo;

        linhas[linha] = suspenso
            ? atual.TrimEnd() + Marca
            : Remover(atual);

        var resultado = string.Join('\n', linhas);
        if (crlf) resultado = resultado.Replace("\n", "\r\n", StringComparison.Ordinal);
        return string.Equals(resultado, conteudo, StringComparison.Ordinal) ? conteudo : resultado;
    }

    /// <summary>O texto sem a marca — é o que se mostra do cartão.</summary>
    public static string Remover(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto;

        var i = texto.IndexOf(Marca, StringComparison.OrdinalIgnoreCase);
        return i < 0 ? texto : (texto[..i] + texto[(i + Marca.Length)..]).TrimEnd();
    }
}
