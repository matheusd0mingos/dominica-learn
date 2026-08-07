using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Ligacoes;

/// <summary>
/// Acrescenta uma ligação a uma nota, numa seção previsível no fim do arquivo.
///
/// POR QUE ISTO EXISTE: ligar duas notas exigia digitar "[[" e lembrar o nome exato. É a sintaxe do
/// Obsidian e ela fica — mas ela não pode ser o ÚNICO caminho, porque um recurso que só aparece para
/// quem já sabe a sintaxe é um recurso invisível. Com isto, o painel da nota ganha botão: escolhe a
/// nota numa busca, e o wikilink é escrito. O "[[" vira atalho de quem gosta, não pedágio.
///
/// E É DOMÍNIO, não infraestrutura, pela mesma razão do favorito: ligar duas notas É UMA EDIÇÃO DE
/// TEXTO MARKDOWN, regida por regras — o que preservar, onde inserir, o que fazer quando já existe.
///
/// —— AS DECISÕES, E POR QUE CADA UMA ————————————————————————————————————————————————
///
/// UMA SEÇÃO FIXA NO FIM, e não "perto de onde faz sentido". Previsível ganha de esperto: você sempre
/// sabe onde procurar, sabe o que apagar, e o Obsidian mostra a mesma coisa. Um inserir inteligente
/// que adivinha o parágrafo certo erra em silêncio e mexe no meio do seu texto.
///
/// O CORPO NÃO É TOCADO. Só se acrescenta linha; nada do que já estava lá é reescrito. É a mesma
/// promessa do EditorDeFrontmatter, e vale ainda mais aqui, porque a nota é o produto.
///
/// NÃO DUPLICA. Ligar duas vezes a mesma nota não escreve duas linhas — devolve o texto intacto e diz
/// que já havia. Sem isso, clicar duas vezes por engano sujaria a nota e o grafo mostraria a mesma
/// aresta contada duas vezes.
///
/// A LIGAÇÃO JÁ EXISTENTE NO CORPO CONTA. Se você escreveu "segue [[Lei 14.133]]" no meio da prosa,
/// o botão não acrescenta a mesma ligação no fim: a conexão já existe, e repeti-la só polui. Esta é a
/// regra que faz o botão e o "[[" inline conviverem em vez de brigarem.
/// </summary>
public static class SecaoDeRelacionadas
{
    /// <summary>
    /// O título da seção. "Relacionadas" e não "Links" ou "Ligações" porque é o que a pessoa lê
    /// depois, no Obsidian, sem contexto nenhum deste app.
    /// </summary>
    public const string Titulo = "## Relacionadas";

    /// <summary>O que aconteceu ao tentar ligar. Existe para a tela poder dizer a verdade.</summary>
    public enum Resultado
    {
        /// <summary>A linha foi acrescentada.</summary>
        Ligou,

        /// <summary>Já havia essa ligação na nota — no corpo ou na seção. Nada foi escrito.</summary>
        JaHavia,
    }

    /// <summary>
    /// Devolve o conteúdo com a ligação para <paramref name="textoDoAlvo"/> acrescentada.
    /// </summary>
    /// <param name="conteudo">A nota inteira, como está no disco.</param>
    /// <param name="textoDoAlvo">
    /// O que vai dentro dos colchetes — já decidido por <see cref="EscritaDeLigacao.MaisCurta"/>, que
    /// sabe quando o nome basta e quando é preciso o caminho por causa de homônimas.
    /// </param>
    /// <param name="ligacoesQueJaExistem">
    /// Os alvos já ligados nesta nota, vindos da análise dela. Recebidos de fora porque analisar é
    /// trabalho de quem já analisou — e porque assim esta função continua sendo só texto.
    /// </param>
    public static (string Conteudo, Resultado Resultado) Ligar(
        string conteudo, string textoDoAlvo, IEnumerable<string> ligacoesQueJaExistem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textoDoAlvo);
        ArgumentNullException.ThrowIfNull(ligacoesQueJaExistem);

        var texto = (conteudo ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        // COMPARAÇÃO SEM CAIXA, igual à do resolvedor: "[[teste]]" e "[[Teste]]" apontam para a mesma
        // nota, e acrescentar a segunda por diferença de maiúscula seria duplicar sem parecer.
        if (ligacoesQueJaExistem.Any(a => string.Equals(a, textoDoAlvo, StringComparison.OrdinalIgnoreCase)))
            return (conteudo ?? string.Empty, Resultado.JaHavia);

        var linha = $"- [[{textoDoAlvo}]]";

        // A seção pode já existir de uma ligação anterior — ou de a pessoa a ter escrito à mão, que é
        // um uso legítimo e não pode ser atropelado com uma segunda seção de mesmo nome.
        var posicao = PosicaoDaSecao(texto);

        if (posicao < 0)
        {
            // NASCE COM LINHA EM BRANCO ANTES do título: sem ela, o "## Relacionadas" cola no último
            // parágrafo e o Markdown de alguns leitores nem o reconhece como cabeçalho.
            var fim = texto.TrimEnd('\n');
            var separador = fim.Length == 0 ? string.Empty : "\n\n";
            return ($"{fim}{separador}{Titulo}\n{linha}\n", Resultado.Ligou);
        }

        // COM A SEÇÃO EXISTINDO, a linha entra no FIM DELA e não logo abaixo do título: a ordem de
        // chegada é informação — as primeiras ligações são as que você fez ao criar a nota.
        var linhas = texto.Split('\n').ToList();
        var i = posicao + 1;
        var ultimaDaLista = posicao;
        while (i < linhas.Count)
        {
            var atual = linhas[i].TrimStart();
            // Outro cabeçalho encerra a seção. Sem isto, ligar numa nota cujo "## Relacionadas" não é
            // a última seção escreveria a linha dentro da seção seguinte.
            if (atual.StartsWith("#", StringComparison.Ordinal)) break;
            if (atual.Length > 0) ultimaDaLista = i;
            i++;
        }

        linhas.Insert(ultimaDaLista + 1, linha);
        return (string.Join('\n', linhas), Resultado.Ligou);
    }

    /// <summary>O índice da linha do título da seção, ou -1. Ignora o que estiver dentro de bloco de código.</summary>
    private static int PosicaoDaSecao(string texto)
    {
        var linhas = texto.Split('\n');
        var dentroDeCodigo = false;

        for (var i = 0; i < linhas.Length; i++)
        {
            var linha = linhas[i].TrimEnd();

            // Bloco de código pode conter qualquer coisa, inclusive um "## Relacionadas" de exemplo —
            // e escrever dentro dele estragaria o bloco. Mesma regra que a análise de notas já aplica.
            if (linha.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                dentroDeCodigo = !dentroDeCodigo;
                continue;
            }
            if (dentroDeCodigo) continue;

            if (string.Equals(linha.TrimEnd(), Titulo, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }
}
