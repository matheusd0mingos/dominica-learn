using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Grafo;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// A REGRA DO "COSTUMAM VIR JUNTO". Ela decide o que o produto oferece para etiquetar uma nota, e uma
/// sugestão errada aqui vira etiqueta errada no arquivo com um clique — por isso ela é domínio testado,
/// e não três linhas dentro de um serviço.
/// </summary>
public class SugestaoDeEtiquetasTests
{
    private static Etiqueta E(string v) => Etiqueta.TentarCriar(v)!;

    private static IReadOnlyList<Etiqueta> Nota(params string[] etiquetas) => [.. etiquetas.Select(E)];

    /// <summary>Monta o grafo a partir das notas e pergunta o que oferecer a quem já tem <paramref name="tem"/>.</summary>
    private static IReadOnlyList<EtiquetaSugerida> Sugerir(
        IEnumerable<IReadOnlyList<Etiqueta>> vault, string[] tem, int limite = 6)
    {
        var (nos, pares) = GrafoDeEtiquetas.Montar(vault);
        return SugestaoDeEtiquetas.Para(nos, pares, [.. tem.Select(E)], limite);
    }

    [Fact]
    public void Oferece_quem_divide_nota_com_o_que_a_nota_ja_tem()
    {
        var s = Sugerir([Nota("tributario", "decorar"), Nota("portugues", "crase")], ["tributario"]);

        var unica = Assert.Single(s);
        Assert.Equal("decorar", unica.Etiqueta.Valor);
        Assert.True(unica.PorCoocorrencia);
    }

    [Fact]
    public void O_que_a_nota_JA_TEM_nunca_volta_como_sugestao()
    {
        // Oferecer o que já está lá é ruído com cara de recomendação — e o clique não faria nada.
        var s = Sugerir([Nota("tributario", "decorar"), Nota("tributario", "decorar")],
                        ["tributario", "decorar"]);

        Assert.DoesNotContain(s, x => x.PorCoocorrencia);
        Assert.DoesNotContain(s, x => x.Etiqueta.Valor is "tributario" or "decorar");
    }

    [Fact]
    public void A_ordem_soma_os_PESOS_e_nao_conta_vizinhas()
    {
        // A DECISÃO QUE FAZ A LISTA SERVIR PARA ALGUMA COISA. "#decorar" divide 3 notas com #tributario;
        // "#teclado" dividiu 1. Contando vizinhas as duas empatariam em 1, e a ordem sairia por acaso.
        var s = Sugerir(
        [
            Nota("tributario", "decorar"),
            Nota("tributario", "decorar"),
            Nota("tributario", "decorar"),
            Nota("tributario", "teclado"),
        ], ["tributario"]);

        Assert.Equal("decorar", s[0].Etiqueta.Valor);
        Assert.Equal(3, s[0].Peso);
        Assert.Equal("teclado", s[1].Etiqueta.Valor);
        Assert.Equal(1, s[1].Peso);
    }

    [Fact]
    public void O_peso_soma_o_parentesco_com_TODAS_as_etiquetas_da_nota()
    {
        // A nota tem #a e #b. "#alvo" divide uma nota com #a e outra com #b: o parentesco dela com ESTA
        // nota é 2, e não 1. Sem somar, uma etiqueta ligada a tudo que a nota tem perderia para uma
        // ligada forte a uma coisa só.
        var s = Sugerir([Nota("a", "alvo"), Nota("b", "alvo"), Nota("a", "outra"), Nota("x", "y")],
                        ["a", "b"]);

        Assert.Equal("alvo", s[0].Etiqueta.Valor);
        Assert.Equal(2, s[0].Peso);
    }

    [Fact]
    public void Sem_NENHUM_parente_cai_nas_mais_usadas_do_vault()
    {
        // O BURACO QUE O NAVEGADOR PEGOU E O CÓDIGO NÃO: uma nota cujas etiquetas são exclusivas dela não
        // coocorre com nada, e o bloco de sugestão sumia inteiro — na nota mais isolada do vault, que é
        // justamente a que mais precisa de um caminho de volta.
        var s = Sugerir([Nota("sozinha"), Nota("popular", "outra"), Nota("popular", "mais")], ["sozinha"]);

        Assert.NotEmpty(s);
        Assert.All(s, x => Assert.False(x.PorCoocorrencia));
        Assert.Equal("popular", s[0].Etiqueta.Valor);
        Assert.Equal(2, s[0].Peso);          // aqui o peso é "em quantas notas ela aparece"
    }

    [Fact]
    public void Nota_sem_etiqueta_nenhuma_recebe_as_mais_usadas()
    {
        var s = Sugerir([Nota("popular", "a"), Nota("popular", "b"), Nota("rara", "c")], []);

        Assert.Equal("popular", s[0].Etiqueta.Valor);
        Assert.False(s[0].PorCoocorrencia);
    }

    [Fact]
    public void A_lista_completa_com_as_mais_usadas_quando_o_parentesco_nao_enche()
    {
        // Um parente só, limite 3: as outras duas vagas vão para as mais usadas, e cada ficha continua
        // sabendo dizer por que está ali.
        var s = Sugerir(
        [
            Nota("tributario", "decorar"),
            Nota("popular", "x"), Nota("popular", "y"), Nota("popular", "z"),
            Nota("media", "x"), Nota("media", "y"),
        ], ["tributario"], limite: 3);

        Assert.Equal(3, s.Count);
        Assert.Equal("decorar", s[0].Etiqueta.Valor);
        Assert.True(s[0].PorCoocorrencia);
        Assert.False(s[1].PorCoocorrencia);
        Assert.Equal("popular", s[1].Etiqueta.Valor);   // a mais usada primeiro
    }

    [Fact]
    public void Etiqueta_usada_UMA_vez_nao_entra_no_complemento()
    {
        // Oferecer a que escapou uma vez, sem parentesco nenhum com esta nota, é ruído com cara de
        // sugestão — e ela seria a maioria do vault de qualquer pessoa.
        var s = Sugerir([Nota("sozinha"), Nota("escapou", "tambem-escapou")], ["sozinha"]);

        Assert.Empty(s);
    }

    [Fact]
    public void A_ordem_e_deterministica_no_empate()
    {
        // Duas aberturas da MESMA nota têm de oferecer as mesmas etiquetas na mesma ordem, ou a mão nunca
        // aprende onde clicar. No empate, alfabético.
        var vault = new[] { Nota("base", "zebra"), Nota("base", "abelha") };

        var a = Sugerir(vault, ["base"]);
        var b = Sugerir(vault.Reverse().ToArray(), ["base"]);

        Assert.Equal(a.Select(x => x.Etiqueta.Valor), b.Select(x => x.Etiqueta.Valor));
        Assert.Equal("abelha", a[0].Etiqueta.Valor);
    }

    [Fact]
    public void Vault_vazio_nao_sugere_nada()
    {
        Assert.Empty(Sugerir([], ["qualquer"]));
    }

    [Fact]
    public void O_limite_e_respeitado()
    {
        var vault = Enumerable.Range(0, 20).Select(i => Nota("base", $"parente{i:00}")).ToList();

        Assert.Equal(4, Sugerir(vault, ["base"], limite: 4).Count);
    }
}
