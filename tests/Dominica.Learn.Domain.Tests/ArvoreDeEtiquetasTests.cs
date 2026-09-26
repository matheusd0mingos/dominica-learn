using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O número no painel de etiquetas vira a medida que a pessoa usa para decidir o que estudar. Um total
/// inflado não dá erro em lugar nenhum — só faz ela achar que já cobriu uma disciplina. Quase todo teste
/// aqui é sobre CONTAGEM.
/// </summary>
public class ArvoreDeEtiquetasTests
{
    private static IReadOnlyList<Etiqueta> Nota(params string[] etiquetas) =>
        etiquetas.Select(e => Etiqueta.TentarCriar(e)!).ToList();

    private static NoDeEtiqueta Achar(IReadOnlyList<NoDeEtiqueta> nos, params string[] caminho)
    {
        var atual = nos.Single(n => string.Equals(n.Rotulo, caminho[0], StringComparison.OrdinalIgnoreCase));
        foreach (var s in caminho.Skip(1))
            atual = atual.Filhos.Single(n => string.Equals(n.Rotulo, s, StringComparison.OrdinalIgnoreCase));
        return atual;
    }

    [Fact]
    public void PaiEFilhoNaMesmaNotaContamUmaVezSo()
    {
        // O CASO QUE JUSTIFICA A CLASSE. Marcar a disciplina e o tópico na mesma nota é como se marca de
        // verdade. Somando contagens por etiqueta, "#direito" mostraria 2 tendo 1 nota.
        var arvore = ArvoreDeEtiquetas.Montar([Nota("#direito", "#direito/penal")]);

        Assert.Equal(1, Achar(arvore, "direito").Total);
    }

    [Fact]
    public void OPaiSomaAsNotasDosFilhos()
    {
        var arvore = ArvoreDeEtiquetas.Montar([
            Nota("#direito/penal"),
            Nota("#direito/tributário"),
            Nota("#português"),
        ]);

        Assert.Equal(2, Achar(arvore, "direito").Total);
    }

    [Fact]
    public void OPaiSemNotaPropriaAparecerNaMesmaAssim()
    {
        // "#direito" nunca foi escrito por ninguém — existe só como prefixo. Some-lo da árvore deixaria
        // "#direito/penal" e "#direito/tributário" soltos na raiz, sem a disciplina que os une.
        var arvore = ArvoreDeEtiquetas.Montar([Nota("#direito/penal")]);

        var direito = Achar(arvore, "direito");
        Assert.Equal(0, direito.Proprias);
        Assert.Equal(1, direito.Total);
        Assert.Single(direito.Filhos);
    }

    [Fact]
    public void ProprioSeparaDoTotal()
    {
        // As três notas marcadas só como "#direito" são as que ficaram sem tópico. É informação útil, e
        // ela some se só existir o total.
        var arvore = ArvoreDeEtiquetas.Montar([
            Nota("#direito"), Nota("#direito"), Nota("#direito"),
            Nota("#direito/penal"),
        ]);

        var direito = Achar(arvore, "direito");
        Assert.Equal(3, direito.Proprias);
        Assert.Equal(4, direito.Total);
    }

    [Fact]
    public void ARepeticaoDentroDaMesmaNotaNaoConta()
    {
        // O analisador devolve a etiqueta uma vez por ocorrência; três "#direito" num texto são uma nota.
        var arvore = ArvoreDeEtiquetas.Montar([Nota("#direito", "#direito", "#direito")]);

        Assert.Equal(1, Achar(arvore, "direito").Proprias);
    }

    [Fact]
    public void CaixaDiferenteEhAMesmaEtiqueta()
    {
        var arvore = ArvoreDeEtiquetas.Montar([Nota("#Direito"), Nota("#direito")]);

        Assert.Single(arvore);
        Assert.Equal(2, arvore[0].Total);
    }

    [Fact]
    public void ORotuloEhSoOUltimoSegmento()
    {
        // Numa árvore indentada, repetir o caminho inteiro em cada nível empurra para fora da tela
        // justamente o que distingue um irmão do outro.
        var arvore = ArvoreDeEtiquetas.Montar([Nota("#direito/administrativo/licitações")]);

        Assert.Equal("licitações", Achar(arvore, "direito", "administrativo", "licitações").Rotulo);
    }

    [Fact]
    public void AMaiorVemPrimeiro()
    {
        var arvore = ArvoreDeEtiquetas.Montar([
            Nota("#português"),
            Nota("#direito"), Nota("#direito"), Nota("#direito"),
        ]);

        Assert.Equal("direito", arvore[0].Rotulo);
    }

    [Fact]
    public void SemEtiquetaNenhumaAArvoreEhVazia()
    {
        Assert.Empty(ArvoreDeEtiquetas.Montar([]));
        Assert.Empty(ArvoreDeEtiquetas.Montar([Nota()]));
    }
}
