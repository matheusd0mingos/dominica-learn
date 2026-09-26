namespace Dominica.Learn.Domain.Cartoes;

/// <summary>Por que um par de pergunta e resposta não pode virar cartão.</summary>
public enum ProblemaDoCartao
{
    FrenteVazia,
    VersoVazio,
    /// <summary>A pergunta já tem "::" ou ":::" — sairiam dois separadores na mesma linha.</summary>
    FrenteTemSeparador,
}

/// <summary>
/// Escreve o texto de um cartão novo no formato do plugin obsidian-spaced-repetition.
///
/// POR QUE ISTO EXISTE, se o formato é só juntar duas strings com "::":
///
/// Porque hoje criar um cartão exige DECORAR a sintaxe, e uma sintaxe que precisa ser decorada é uma
/// sintaxe que não se usa. Quem está estudando não vai parar no meio de um resumo para lembrar se são
/// dois ou três dois-pontos, se o de várias linhas usa "?" ou "??", e onde exatamente cada um vai. O
/// resultado prático é que os cartões nunca são criados — o recurso existe no código e não na vida.
///
/// E porque a escolha entre as QUATRO FORMAS é uma regra, não um detalhe de tela:
///
///     Pergunta::Resposta        uma linha, um sentido
///     Pergunta:::Resposta       uma linha, os dois sentidos
///     Pergunta / ? / Resposta   várias linhas, um sentido
///     Pergunta / ?? / Resposta  várias linhas, os dois sentidos
///
/// A forma de uma linha é a boa: cabe na leitura do resumo sem quebrar o texto. Ela deixa de servir no
/// instante em que a resposta tem mais de uma linha — e aí escrevê-la assim mesmo produziria um cartão
/// truncado, que só se descobre no dia da revisão. Quem decide é esta classe, olhando o conteúdo.
///
/// PURA de propósito: o texto do cartão é a mesma coisa em qualquer tela, e testá-lo não deve exigir
/// abrir um editor.
/// </summary>
public static class RedatorDeCartao
{
    public static bool TentarEscrever(
        string? frente, string? verso, bool nosDoisSentidos,
        out string? texto, out ProblemaDoCartao? problema)
    {
        texto = null;
        problema = null;

        var f = (frente ?? string.Empty).Trim();
        var v = (verso ?? string.Empty).Trim();

        if (f.Length == 0) { problema = ProblemaDoCartao.FrenteVazia; return false; }
        if (v.Length == 0) { problema = ProblemaDoCartao.VersoVazio; return false; }

        // "::" na pergunta partiria o cartão no lugar errado: o analisador corta no PRIMEIRO separador,
        // e a resposta escrita viraria parte da pergunta. Recusar é melhor que gravar um cartão que já
        // nasce lendo ao contrário.
        if (f.Contains("::", StringComparison.Ordinal))
        {
            problema = ProblemaDoCartao.FrenteTemSeparador;
            return false;
        }

        // A forma de UMA LINHA só serve enquanto os dois lados couberem numa. Escrevê-la mesmo assim
        // produziria um cartão truncado — e truncado de um jeito que só se descobre no dia da revisão,
        // com a resposta pela metade.
        //
        // O SEGUNDO CASO É MAIS TRAIÇOEIRO: um ":" colado no separador o transforma em ":::", que é a
        // marca de "vale nos dois sentidos". Uma resposta como ": ver art. 5º" viraria um cartão de mão
        // dupla, com o ":" comido, sem ninguém ter pedido nada — e o cartão inverso ("ver art. 5º" →
        // pergunta) apareceria na revisão semanas depois, sem explicação. Na dúvida, forma de bloco.
        var precisaDeBloco = f.Contains('\n') || v.Contains('\n') || f.EndsWith(':') || v.StartsWith(':');

        texto = precisaDeBloco
            ? $"{f}\n{(nosDoisSentidos ? "??" : "?")}\n{v}\n"
            : $"{f}{(nosDoisSentidos ? ":::" : "::")}{v}\n";

        return true;
    }
}
