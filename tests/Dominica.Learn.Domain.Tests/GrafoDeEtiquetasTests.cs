using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Grafo;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O mapa das etiquetas responde uma pergunta que o grafo de notas não responde: "o que anda junto".
/// Ninguém escreveu essas ligações — elas só existem no conjunto.
/// </summary>
public class GrafoDeEtiquetasTests
{
    private static IReadOnlyList<Etiqueta> Nota(params string[] etiquetas) =>
        etiquetas.Select(e => Etiqueta.TentarCriar(e)!).ToList();

    [Fact]
    public void Duas_etiquetas_na_mesma_nota_viram_par()
    {
        var (nos, pares) = GrafoDeEtiquetas.Montar([Nota("direito", "tributario")]);

        Assert.Equal(2, nos.Count);
        Assert.Equal(1, Assert.Single(pares).Peso);
    }

    [Fact]
    public void O_peso_conta_em_quantas_notas_elas_dividem_espaco()
    {
        var (_, pares) = GrafoDeEtiquetas.Montar(
            [Nota("decorar", "tributario"), Nota("decorar", "tributario"), Nota("decorar", "portugues")]);

        Assert.Equal(2, pares.Count);                       // decorar–tributario e decorar–portugues
        Assert.Equal(2, pares.Max(p => p.Peso));
    }

    [Fact]
    public void Etiqueta_repetida_na_MESMA_nota_conta_uma_vez()
    {
        // Escrever "#direito" três vezes num texto não é três notas. Contar repetição inflaria o ponto
        // por prolixidade em vez de por alcance — e aí o mapa mediria estilo de escrita, não alcance.
        var (nos, pares) = GrafoDeEtiquetas.Montar([Nota("direito", "direito", "direito", "tributario")]);

        Assert.Equal(2, nos.Count);
        Assert.Equal(1, nos.Single(n => n.Etiqueta.Valor == "direito").Notas);
        Assert.Equal(1, Assert.Single(pares).Peso);
    }

    [Fact]
    public void O_par_e_guardado_UMA_vez_e_nao_dois()
    {
        // "#a anda com #b" é a mesma frase ao contrário. Duas arestas sobre a mesma dupla sairiam com o
        // dobro da grossura na tela — o mapa mentiria sobre a força da relação.
        var (_, pares) = GrafoDeEtiquetas.Montar([Nota("b", "a")]);

        var p = Assert.Single(pares);
        Assert.True(p.De < p.Para);
    }

    [Fact]
    public void Etiqueta_sozinha_aparece_sem_vizinhas()
    {
        // Ela precisa aparecer: é a etiqueta que você usa e que não se conecta a nada — informação, e
        // não ausência de informação.
        var (nos, pares) = GrafoDeEtiquetas.Montar([Nota("solta"), Nota("a", "b")]);

        var solta = nos.Single(n => n.Etiqueta.Valor == "solta");
        Assert.Equal(1, solta.Notas);
        Assert.Equal(0, solta.Vizinhas);
        Assert.Single(pares);
    }

    [Fact]
    public void A_ordem_e_deterministica()
    {
        // Um mapa que muda de desenho sozinho destrói a memória espacial, que é a única razão de um
        // grafo existir. As mesmas notas em ordem diferente têm de dar o mesmo mapa.
        var a = GrafoDeEtiquetas.Montar([Nota("z", "a"), Nota("m", "a")]);
        var b = GrafoDeEtiquetas.Montar([Nota("m", "a"), Nota("a", "z")]);

        Assert.Equal(a.Nos.Select(n => n.Etiqueta.Valor), b.Nos.Select(n => n.Etiqueta.Valor));
        Assert.Equal(a.Pares.Select(p => (p.De, p.Para, p.Peso)), b.Pares.Select(p => (p.De, p.Para, p.Peso)));
    }

    [Fact]
    public void Vault_sem_etiqueta_da_mapa_vazio()
    {
        var (nos, pares) = GrafoDeEtiquetas.Montar([]);
        Assert.Empty(nos);
        Assert.Empty(pares);
    }
}
