using Dominica.Learn.Domain.Grafo;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O enquadramento existe para o vault PEQUENO — o de quem está começando. Um layout de 900×620 com
/// três notas vira três pontos minúsculos nos cantos, e essa é a primeira impressão que o produto dá
/// justamente para quem ainda pode desistir. Os testes guardam os casos degenerados, que são onde uma
/// conta de moldura costuma explodir.
/// </summary>
public class EnquadramentoTests
{
    private const double L = 900, A = 620;

    private static PosicaoDoNo P(double x, double y, double r = 6) => new(0, x, y, r);

    [Fact]
    public void VaultVazioDevolveAMolduraInteira()
    {
        // Janela de tamanho zero faria o SVG sumir da tela.
        var e = Enquadramento.Ajustar([], L, A);

        Assert.Equal(new Enquadramento(0, 0, L, A), e);
    }

    [Fact]
    public void UmNoSoNaoGeraJanelaDegenerada()
    {
        var e = Enquadramento.Ajustar([P(450, 310)], L, A);

        Assert.True(e.Largura >= 120, "janela estreita demais faria o zoom explodir");
        Assert.True(e.Altura >= 120 * A / L * 0.99);
        Assert.InRange(450, e.X, e.X + e.Largura);   // o nó está dentro
        Assert.InRange(310, e.Y, e.Y + e.Altura);
    }

    [Fact]
    public void NosEmpilhadosNoMesmoPontoNaoGeramJanelaZero()
    {
        var e = Enquadramento.Ajustar([P(100, 100), P(100, 100), P(100, 100)], L, A);

        Assert.True(e.Largura > 0 && e.Altura > 0);
    }

    [Fact]
    public void NosProximosGeramJanelaMenorQueAMoldura()
    {
        // É o caso que motivou a classe: três notas perto umas das outras.
        var e = Enquadramento.Ajustar([P(400, 300), P(430, 320), P(460, 290)], L, A);

        Assert.True(e.Largura < L, "a janela devia apertar em volta do que existe");
    }

    [Fact]
    public void NuncaAmpliaAlemDoDesenhoOriginal()
    {
        // Enquadrar é apertar a janela, não inflar o conteúdo: pontos gigantes com rótulo pequeno ao
        // lado parecem defeito.
        var e = Enquadramento.Ajustar([P(10, 10), P(890, 610)], L, A);

        Assert.True(e.Largura <= L + 0.01);
        Assert.True(e.Altura <= A + 0.01);
    }

    [Fact]
    public void TodosOsNosCabemDentroDaJanela()
    {
        var nos = new[] { P(120, 90), P(700, 480), P(300, 550), P(880, 60) };

        var e = Enquadramento.Ajustar(nos, L, A);

        foreach (var n in nos)
        {
            Assert.InRange(n.X, e.X, e.X + e.Largura);
            Assert.InRange(n.Y, e.Y, e.Y + e.Altura);
        }
    }

    [Fact]
    public void ProporcaoAcompanhaADaTela()
    {
        // Se a janela não casar com a proporção da área desenhada, o SVG ajusta sozinho e sobra faixa
        // vazia de um lado — a folga pedida vira outra coisa.
        var e = Enquadramento.Ajustar([P(400, 300), P(500, 320)], L, A);

        Assert.Equal(L / A, e.Largura / e.Altura, precision: 2);
    }

    [Fact]
    public void ViewBoxSaiComPontoDecimalEmQualquerCultura()
    {
        var antes = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // pt-BR escreve 1,5 — e "1,5" num viewBox faz o SVG inteiro não desenhar, calado.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            var texto = new Enquadramento(1.5, 2.25, 300, 200).ParaViewBox();

            Assert.Equal("1.5 2.25 300 200", texto);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = antes; }
    }
}
