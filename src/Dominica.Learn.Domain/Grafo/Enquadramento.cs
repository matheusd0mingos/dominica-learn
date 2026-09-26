using System.Globalization;

namespace Dominica.Learn.Domain.Grafo;

/// <summary>
/// A janela que a tela deve mostrar do desenho — o <c>viewBox</c> de um SVG.
///
/// POR QUE ISTO EXISTE, em vez de a tela usar a moldura inteira do layout:
///
/// O layout espalha os nós numa área fixa (900×620). Num vault com trezentas notas ele a preenche, e
/// mostrar a moldura inteira é o certo. Num vault com TRÊS, o mesmo layout deixa três pontos minúsculos
/// jogados nos cantos de um retângulo vazio — que é exatamente a impressão de "não funcionou" no dia em
/// que a pessoa está começando, ou seja, no único dia em que ela ainda pode desistir.
///
/// Enquadrar é apertar a janela em volta do que existe. O desenho não muda; muda quanto dele se vê.
/// A propriedade que o layout garante — mesmo vault, mesmo desenho — continua valendo, porque isto é
/// função pura das posições.
/// </summary>
public sealed record Enquadramento(double X, double Y, double Largura, double Altura)
{
    /// <summary>Como o SVG quer: "x y largura altura", sempre com ponto decimal.</summary>
    public string ParaViewBox() => string.Create(
        CultureInfo.InvariantCulture, $"{X:0.##} {Y:0.##} {Largura:0.##} {Altura:0.##}");

    /// <summary>
    /// A janela que mostra todos os nós com uma folga em volta.
    /// </summary>
    /// <param name="proporcao">
    /// Largura ÷ altura da área na tela. O enquadramento é esticado para casar com ela — sem isso, o
    /// SVG faria o ajuste sozinho sobrando faixa vazia de um lado, e a folga pedida viraria outra coisa.
    /// </param>
    public static Enquadramento Ajustar(
        IReadOnlyList<PosicaoDoNo> posicoes, double larguraPadrao, double alturaPadrao, double folga = 40)
    {
        // Vault vazio: não há o que enquadrar, e uma janela de tamanho zero faria o SVG desaparecer.
        if (posicoes is null || posicoes.Count == 0)
            return new Enquadramento(0, 0, larguraPadrao, alturaPadrao);

        var minX = posicoes.Min(p => p.X - p.Raio);
        var maxX = posicoes.Max(p => p.X + p.Raio);
        var minY = posicoes.Min(p => p.Y - p.Raio);
        var maxY = posicoes.Max(p => p.Y + p.Raio);

        var largura = maxX - minX + folga * 2;
        var altura = maxY - minY + folga * 2;
        var x = minX - folga;
        var y = minY - folga;

        // UM nó só, ou todos empilhados no mesmo ponto: a caixa degenera e o zoom viraria infinito.
        // O piso não é estético — é o que impede uma divisão por zero e um ponto do tamanho da tela.
        const double minimo = 120;
        if (largura < minimo) { x -= (minimo - largura) / 2; largura = minimo; }
        if (altura < minimo) { y -= (minimo - altura) / 2; altura = minimo; }

        // Nunca ampliar além do desenho original: com quatro notas próximas, esticar até preencher a
        // tela deixaria os pontos gigantes e o texto do rótulo pequeno ao lado deles — desproporção que
        // parece defeito. Enquadrar é para APERTAR a janela, não para inflar o conteúdo.
        largura = Math.Min(largura, larguraPadrao);
        altura = Math.Min(altura, alturaPadrao);

        // Casa a proporção da área da tela, crescendo o lado que falta. Crescer (e nunca cortar) é o
        // que garante que nenhum nó fique de fora do que foi enquadrado.
        var proporcaoAlvo = larguraPadrao / alturaPadrao;
        if (largura / altura < proporcaoAlvo)
        {
            var nova = altura * proporcaoAlvo;
            x -= (nova - largura) / 2;
            largura = nova;
        }
        else
        {
            var nova = largura / proporcaoAlvo;
            y -= (nova - altura) / 2;
            altura = nova;
        }

        return new Enquadramento(x, y, largura, altura);
    }
}
