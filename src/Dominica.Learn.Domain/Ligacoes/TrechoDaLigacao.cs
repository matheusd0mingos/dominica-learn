namespace Dominica.Learn.Domain.Ligacoes;

/// <summary>
/// O TRECHO EM VOLTA DE UMA LIGAÇÃO — a frase que faz um backlink dizer alguma coisa.
///
/// "Licitações aponta para cá" é metade da informação; a outra metade é COMO ela aponta: "o prazo
/// corre conforme [[Prescrição]], salvo…". É o que o painel de backlinks do Obsidian mostra, e o que
/// faz a pessoa VER a conexão sem abrir a outra nota. As menções não ligadas já tinham o trecho; as
/// ligadas, ironicamente, não — esta classe fecha essa assimetria.
///
/// A JANELA NÃO ATRAVESSA A LINHA: atravessar juntaria pedaços de parágrafos distintos numa "frase"
/// que ninguém escreveu. Linha de Markdown é a unidade natural da prosa aqui.
/// </summary>
public static class TrechoDaLigacao
{
    /// <summary>Contexto de cada lado — o mesmo tamanho do trecho das menções, para as listas casarem.</summary>
    public const int ContextoEmCaracteres = 60;

    public static string EmVolta(string conteudo, int posicao, int comprimento)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        if (conteudo.Length == 0) return string.Empty;

        posicao = Math.Clamp(posicao, 0, conteudo.Length - 1);
        comprimento = Math.Max(0, comprimento);

        var inicioDaLinha = conteudo.LastIndexOf('\n', posicao) + 1;   // -1 + 1 = 0 na primeira linha
        var fimDaLinha = conteudo.IndexOf('\n', posicao);
        if (fimDaLinha < 0) fimDaLinha = conteudo.Length;

        var de = Math.Max(inicioDaLinha, posicao - ContextoEmCaracteres);
        var ate = Math.Min(fimDaLinha, posicao + comprimento + ContextoEmCaracteres);

        var recorte = conteudo[de..ate].Trim();
        return (de > inicioDaLinha ? "…" : "") + recorte + (ate < fimDaLinha ? "…" : "");
    }
}
