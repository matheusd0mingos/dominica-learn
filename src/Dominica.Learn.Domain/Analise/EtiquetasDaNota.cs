namespace Dominica.Learn.Domain.Analise;

/// <summary>De onde uma etiqueta da nota veio. A origem decide o que dá para fazer com ela.</summary>
public enum OrigemDaEtiqueta
{
    /// <summary>Está no frontmatter, em "tags:". Dá para tirar com um clique.</summary>
    Frontmatter,

    /// <summary>Está escrita no meio do texto, como "#decorar". Só sai editando a frase.</summary>
    Texto,
}

/// <summary>Uma etiqueta da nota e de onde ela veio.</summary>
public sealed record EtiquetaDaNota(Etiqueta Etiqueta, OrigemDaEtiqueta Origem)
{
    /// <summary>Só o que está no frontmatter pode ser tirado sem mexer na prosa de alguém.</summary>
    public bool PodeTirar => Origem == OrigemDaEtiqueta.Frontmatter;
}

/// <summary>
/// PÔR E TIRAR ETIQUETA DE UMA NOTA sem digitar no meio do texto.
///
/// POR QUE ISTO EXISTE: até aqui, etiquetar era escrever "#alguma-coisa" no corpo da nota — e mais nada.
/// Quem não conhece a sintaxe não etiqueta; quem conhece não tem como VER as etiquetas da nota aberta sem
/// procurá-las com o olho no texto, nem como tirar uma sem caçar a palavra. É o mesmo defeito que o botão
/// de ligar resolveu para o "[[": a sintaxe fica, mas ela não pode ser o único caminho.
///
/// —— AS DECISÕES ————————————————————————————————————————————————————————————————————
///
/// ESCREVE NO FRONTMATTER, e não no fim do corpo. Três razões, e a terceira é a que decide:
///   • é onde o Obsidian põe as dele, então a nota continua legível lá — e o vault é do usuário;
///   • frontmatter é metadado; enfiar "#tag #tag #tag" no rodapé mistura metadado com leitura;
///   • o corpo é o que a pessoa escreveu. Um botão que acrescenta linha no texto de alguém para
///     guardar metadado nosso é o tipo de coisa que se descobre tarde, quando já está em 300 notas.
///
/// TIRAR SÓ TIRA DO FRONTMATTER. Uma etiqueta escrita no meio de uma frase — "isso é #pegadinha clássica"
/// — não pode sumir por clique: apagá-la reescreveria a frase, e uma tela que edita a prosa de alguém por
/// baixo do pano perde a confiança de uma vez. Ela APARECE, com a origem à mostra, e o "tirar" fica
/// desabilitado. Honesto ganha de conveniente aqui.
///
/// PÔR O QUE JÁ EXISTE NÃO DUPLICA — inclusive quando a que existe está no texto. Marcar "#decorar" numa
/// nota que já diz "#decorar" no meio de um parágrafo acrescentaria a mesma etiqueta duas vezes: no
/// painel ela apareceria uma vez só (a análise já faz Distinct), mas o arquivo ficaria com a informação
/// repetida em dois lugares — e um dia alguém apagaria uma das duas achando que era sobra.
///
/// A CLASSE É PURA: recebe o texto da nota, devolve o texto da nota. Nada de disco, nada de índice.
/// </summary>
public static class EtiquetasDaNota
{
    /// <summary>A chave onde as etiquetas são ESCRITAS. É a que o Obsidian usa.</summary>
    public const string Campo = "tags";

    /// <summary>
    /// As chaves LIDAS. "tag" no singular existe em vault antigo e em quem digitou errado uma vez — ler
    /// só "tags" faria essas etiquetas aparecerem no painel (o analisador lê as duas) e não aqui, e o
    /// "tirar" não acharia nada para tirar.
    /// </summary>
    private static readonly string[] CamposLidos = ["tags", "tag"];

    /// <summary>
    /// As etiquetas da nota, com a origem de cada uma. A ordem é: as do frontmatter primeiro, na ordem
    /// em que estão escritas, depois as do texto — porque é a ordem em que se pode agir sobre elas.
    /// </summary>
    /// <param name="analise">A análise da nota, que já sabe todas as etiquetas (corpo + frontmatter).</param>
    public static IReadOnlyList<EtiquetaDaNota> De(AnaliseDaNota analise)
    {
        ArgumentNullException.ThrowIfNull(analise);

        var noFrontmatter = DoFrontmatter(analise.Frontmatter);
        var jaVistas = noFrontmatter.ToHashSet();

        return
        [
            .. noFrontmatter.Select(e => new EtiquetaDaNota(e, OrigemDaEtiqueta.Frontmatter)),
            .. analise.Etiquetas.Where(e => !jaVistas.Contains(e))
                                .Select(e => new EtiquetaDaNota(e, OrigemDaEtiqueta.Texto)),
        ];
    }

    /// <summary>As etiquetas escritas no bloco de metadados, na ordem em que estão lá.</summary>
    public static IReadOnlyList<Etiqueta> DoFrontmatter(Frontmatter frontmatter)
    {
        ArgumentNullException.ThrowIfNull(frontmatter);

        var etiquetas = new List<Etiqueta>();
        foreach (var chave in CamposLidos)
            foreach (var bruta in frontmatter.Lista(chave))
                if (Etiqueta.TentarCriar(bruta) is { } e && !etiquetas.Contains(e))
                    etiquetas.Add(e);

        return etiquetas;
    }

    /// <summary>O que aconteceu ao pôr ou tirar. Existe para a tela poder dizer a verdade.</summary>
    public enum Resultado
    {
        /// <summary>O arquivo mudou.</summary>
        Mudou,

        /// <summary>A nota já tinha essa etiqueta. Nada foi escrito.</summary>
        JaTinha,

        /// <summary>A nota não tinha essa etiqueta no frontmatter — ou ela está no texto. Nada foi escrito.</summary>
        NaoTinha,
    }

    /// <summary>
    /// Acrescenta a etiqueta ao frontmatter, se ela ainda não estiver na nota.
    /// </summary>
    /// <param name="jaExistentes">
    /// As etiquetas que a nota já tem, vindas da análise dela — as do texto inclusive. Recebidas de fora
    /// porque analisar é trabalho de quem já analisou, e porque assim esta função continua sendo só
    /// texto. Mesmo arranjo do <see cref="Ligacoes.SecaoDeRelacionadas"/>.
    /// </param>
    public static (string Conteudo, Resultado Resultado) Marcar(
        string conteudo, Etiqueta etiqueta, IEnumerable<Etiqueta> jaExistentes)
    {
        ArgumentNullException.ThrowIfNull(etiqueta);
        ArgumentNullException.ThrowIfNull(jaExistentes);

        var texto = conteudo ?? string.Empty;

        // JÁ ESTÁ NA NOTA — no frontmatter ou no meio do texto. Nos dois casos a etiqueta já vale, e
        // escrevê-la de novo só duplicaria a informação em dois lugares do mesmo arquivo.
        if (jaExistentes.Contains(etiqueta)) return (texto, Resultado.JaTinha);

        var atuais = DoFrontmatter(LerFrontmatter(texto)).Select(e => e.Valor).ToList();
        atuais.Add(etiqueta.Valor);

        return (EditorDeFrontmatter.DefinirLista(texto, Campo, atuais), Resultado.Mudou);
    }

    /// <summary>
    /// Tira a etiqueta do frontmatter. Etiqueta que está no TEXTO não sai por aqui — ver o resumo da
    /// classe: apagar do meio de uma frase é reescrever o que a pessoa escreveu.
    /// </summary>
    public static (string Conteudo, Resultado Resultado) Desmarcar(string conteudo, Etiqueta etiqueta)
    {
        ArgumentNullException.ThrowIfNull(etiqueta);

        var texto = conteudo ?? string.Empty;
        var frontmatter = LerFrontmatter(texto);
        if (!DoFrontmatter(frontmatter).Contains(etiqueta)) return (texto, Resultado.NaoTinha);

        var novo = texto;

        // TIRA DAS DUAS CHAVES. Estando em "tags" e em "tag", limpar só uma faria o clique parecer que
        // não funcionou: a ficha sumiria e voltaria na releitura da nota.
        foreach (var chave in CamposLidos)
        {
            var restantes = LerFrontmatter(novo).Lista(chave)
                .Select(Etiqueta.TentarCriar)
                .Where(e => e is not null && e != etiqueta)
                .Select(e => e!.Valor)
                .ToList();

            // Só reescreve a chave que existe: passar lista vazia numa chave ausente é inofensivo, mas
            // passar por ela à toa reformata o campo do usuário sem necessidade.
            if (LerFrontmatter(novo).Lista(chave).Count > 0)
                novo = EditorDeFrontmatter.DefinirLista(novo, chave, restantes);
        }

        return (novo, Resultado.Mudou);
    }

    private static Frontmatter LerFrontmatter(string texto) =>
        Frontmatter.Ler((texto ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
}
