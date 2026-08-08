using System.Text;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Ligacoes;

/// <summary>
/// A NOTA-ÍNDICE (o "MOC" — Map of Content — da cultura Obsidian): uma nota comum, cheia de links, que
/// dá a visão de um assunto inteiro.
///
/// É a peça que faz a estrutura EMERGIR DE BAIXO em vez de depender das pastas: a pasta diz onde o
/// arquivo mora, o índice diz como o assunto se organiza — e um assunto (#prazo, #pegadinha) atravessa
/// pastas. No grafo, o índice vira o CENTRO do assunto: todas as notas dele apontadas por um nó só.
///
/// E É UMA NOTA COMUM DE PROPÓSITO, não uma tela: abre no Obsidian, aceita anotação em volta dos links,
/// entra no grafo e pode até ter cartões. A máquina só monta o primeiro rascunho — a curadoria (ordenar,
/// comentar, tirar) é da pessoa, e é ela que transforma uma lista num mapa.
/// </summary>
public static class NotaIndice
{
    /// <summary>
    /// Onde o índice de uma etiqueta mora — na raiz, "Índice — prazo.md". Na raiz porque o assunto
    /// atravessa as pastas; com "Índice — " na frente para o grupo se reconhecer em qualquer listagem.
    /// A barra da etiqueta hierárquica vira "·": "/" no nome criaria uma subpasta sem querer.
    /// </summary>
    public static CaminhoNota CaminhoDe(Etiqueta etiqueta)
    {
        ArgumentNullException.ThrowIfNull(etiqueta);
        return CaminhoNota.De($"Índice — {etiqueta.Valor.Replace('/', '·')}.md");
    }

    /// <summary>
    /// O primeiro rascunho do índice: título, a própria etiqueta (o índice é PARTE do assunto, e é isso
    /// que o pendura no mapa de etiquetas), e um link por nota. Os links vêm prontos de quem chama —
    /// escolher entre "[[Nome]]" e "[[Pasta/Nome]]" exige conhecer o vault inteiro (ver EscritaDeLigacao).
    /// </summary>
    public static string Conteudo(Etiqueta etiqueta, IReadOnlyList<string> links)
    {
        ArgumentNullException.ThrowIfNull(etiqueta);
        ArgumentNullException.ThrowIfNull(links);

        var sb = new StringBuilder();
        sb.Append("# Índice — ").Append(etiqueta).Append("\n\n");
        sb.Append(etiqueta).Append("\n\n");
        sb.Append("O mapa deste assunto. A lista nasceu da etiqueta; reordene, comente e corte à vontade — ")
          .Append("a curadoria é o que transforma a lista num mapa.\n\n");
        foreach (var link in links)
            sb.Append("- [[").Append(link).Append("]]\n");
        return sb.ToString();
    }
}
