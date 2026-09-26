using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Reconciliacao;

/// <summary>
/// Compara o que está no DISCO com o que o ÍNDICE lembra e diz o que mudou.
///
/// ESTA CLASSE É A PEÇA CENTRAL DA ARQUITETURA, e vale explicar por quê.
///
/// A especificação pede duas coisas que juntas definem tudo: as notas ficam em .md no storage, e o vault
/// tem de abrir no Obsidian Desktop. A segunda implica que os arquivos MUDAM PELAS COSTAS DA APLICAÇÃO —
/// por um Obsidian aberto no notebook, por um `git pull`, por um sincronizador de nuvem, por um `mv` no
/// terminal às duas da manhã. Não é cenário excepcional: é o uso normal do produto.
///
/// A consequência arquitetural é uma só, e ela precisa estar escrita em algum lugar antes que alguém
/// tente resolver isso com um TRIGGER no Postgres:
///
///     O DISCO É A FONTE DA VERDADE. O ÍNDICE É DERIVADO E DESCARTÁVEL.
///
/// Apagar o banco inteiro e reconstruí-lo a partir do vault tem de ser uma operação de rotina, não um
/// desastre. Nada que exista só no índice pode ser conhecimento do usuário — se um dado não sobrevive à
/// reconstrução, ele não pertence ao índice, pertence ao arquivo .md.
///
/// A comparação é uma FUNÇÃO PURA de duas listas para uma lista. Nenhum IO, nenhum relógio: dá para
/// testar todo caso difícil (renomeação em massa, troca de nomes entre dois arquivos, pasta inteira
/// movida) com duas listas literais e sem tocar em disco nenhum.
/// </summary>
public static class Reconciliador
{
    /// <summary>
    /// Compara os dois lados. Determinístico: mesma entrada, mesma saída, na mesma ordem.
    /// </summary>
    public static ResultadoDaReconciliacao Comparar(
        IEnumerable<EstadoDaNota> noDisco,
        IEnumerable<EstadoDaNota> noIndice)
    {
        var disco = noDisco.ToDictionary(e => e.Caminho);
        var indice = noIndice.ToDictionary(e => e.Caminho);

        var criadas = new List<EstadoDaNota>();
        var alteradas = new List<EstadoDaNota>();
        var removidas = new List<EstadoDaNota>();

        foreach (var (caminho, doDisco) in disco)
        {
            if (!indice.TryGetValue(caminho, out var doIndice)) { criadas.Add(doDisco); continue; }
            // A DATA NÃO DECIDE NADA — só a impressão digital. Sincronizador de nuvem reescreve mtime de
            // arquivo intacto, e `git checkout` carimba a hora do checkout em tudo. Confiar na data faria
            // o vault inteiro parecer alterado depois de qualquer uma dessas operações, e reindexar dez
            // mil notas à toa é o tipo de lentidão que faz a ferramenta ser abandonada.
            if (!doDisco.Impressao.Equals(doIndice.Impressao)) alteradas.Add(doDisco);
        }

        foreach (var (caminho, doIndice) in indice)
            if (!disco.ContainsKey(caminho)) removidas.Add(doIndice);

        // —— RENOMEAÇÃO: sumiu de um lugar, apareceu em outro, mesmo conteúdo ————————————————
        // Sem este pareamento, mover uma nota (ou arrastar uma pasta inteira no Obsidian) seria lido como
        // "apagou 40 notas, criou 40 notas" — e levaria junto o histórico, os favoritos e as estatísticas
        // de estudo de cada uma. O que é a mesma coisa que perdê-los.
        var divergencias = new List<Divergencia>();
        var criadasPorImpressao = Agrupar(criadas);
        var removidasPorImpressao = Agrupar(removidas);

        var criadasPareadas = new HashSet<CaminhoNota>();
        var removidasPareadas = new HashSet<CaminhoNota>();

        foreach (var (impressao, saidas) in removidasPorImpressao.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!criadasPorImpressao.TryGetValue(impressao, out var entradas)) continue;
            // Conteúdo duplicado torna o pareamento ambíguo (duas notas idênticas trocando de pasta). A
            // ordem alfabética dos dois lados não é "a resposta certa" — não existe uma — mas é ESTÁVEL,
            // e reconciliação que dá resultado diferente a cada execução é pior que qualquer pareamento.
            var de = saidas.OrderBy(c => c.Valor, StringComparer.Ordinal).ToList();
            var para = entradas.OrderBy(c => c.Valor, StringComparer.Ordinal).ToList();
            for (var i = 0; i < Math.Min(de.Count, para.Count); i++)
            {
                divergencias.Add(new Divergencia(TipoDeDivergencia.Renomeada, para[i], de[i]));
                removidasPareadas.Add(de[i]);
                criadasPareadas.Add(para[i]);
            }
        }

        foreach (var e in criadas.Where(e => !criadasPareadas.Contains(e.Caminho)).OrderBy(e => e.Caminho.Valor, StringComparer.Ordinal))
            divergencias.Add(new Divergencia(TipoDeDivergencia.Criada, e.Caminho));

        foreach (var e in alteradas.OrderBy(e => e.Caminho.Valor, StringComparer.Ordinal))
            divergencias.Add(new Divergencia(TipoDeDivergencia.Alterada, e.Caminho));

        foreach (var e in removidas.Where(e => !removidasPareadas.Contains(e.Caminho)).OrderBy(e => e.Caminho.Valor, StringComparer.Ordinal))
            divergencias.Add(new Divergencia(TipoDeDivergencia.Removida, e.Caminho));

        return divergencias.Count == 0 ? ResultadoDaReconciliacao.Nenhuma : new ResultadoDaReconciliacao(divergencias);
    }

    private static Dictionary<string, List<CaminhoNota>> Agrupar(List<EstadoDaNota> estados)
    {
        var mapa = new Dictionary<string, List<CaminhoNota>>(StringComparer.Ordinal);
        foreach (var e in estados)
        {
            if (!mapa.TryGetValue(e.Impressao.Valor, out var lista)) mapa[e.Impressao.Valor] = lista = [];
            lista.Add(e.Caminho);
        }
        return mapa;
    }
}
