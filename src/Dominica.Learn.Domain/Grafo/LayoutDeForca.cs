namespace Dominica.Learn.Domain.Grafo;

/// <summary>Onde cada nó do grafo fica desenhado, e de que tamanho.</summary>
public sealed record PosicaoDoNo(int Indice, double X, double Y, double Raio);

/// <summary>O grafo com posições — pronto para virar SVG sem mais nenhuma conta.</summary>
public sealed record GrafoPosicionado(
    GrafoDoVault Grafo, IReadOnlyList<PosicaoDoNo> Posicoes, double Largura, double Altura);

/// <summary>
/// Calcula as posições do grafo por força dirigida (Fruchterman-Reingold), EM C#.
///
/// POR QUE NÃO UMA BIBLIOTECA JS DE GRAFO — e esta é a decisão que vale explicar:
///
/// 1. A especificação pede para evitar JavaScript. Aqui dá para obedecer de verdade: layout é
///    matemática, e matemática não precisa de navegador. O que sai daqui é uma lista de coordenadas que
///    vira SVG estático.
///
/// 2. É DETERMINÍSTICO. Bibliotecas de grafo animam a simulação a partir de posições aleatórias, então o
///    mesmo vault desenha diferente a cada abertura. Num grafo de conhecimento isso é sabotagem: a
///    memória espacial — "o aglomerado de Direito fica no canto esquerdo" — é metade do valor da visão.
///    Aqui a posição inicial vem de um hash do CAMINHO da nota, não de um sorteio: mesmo vault, mesmo
///    desenho, sempre, em qualquer máquina.
///
/// 3. Testável. Um layout que roda no navegador só se verifica com o olho.
///
/// O preço: sem animação e sem arrastar nó com o mouse. Para a pergunta que o grafo responde — "o que
/// cerca isto?", "o que ficou órfão?" — nenhum dos dois faz falta.
/// </summary>
public static class LayoutDeForca
{
    private const int IteracoesPadrao = 300;

    /// <summary>
    /// Quanto o centro puxa, em relação à força de uma ligação. Baixo de propósito: o suficiente para que
    /// nada escape do quadro, longe do bastante para não achatar os aglomerados contra o meio.
    /// </summary>
    private const double PesoDaGravidade = 0.15;

    public static GrafoPosicionado Calcular(
        GrafoDoVault grafo, double largura = 800, double altura = 600, int iteracoes = IteracoesPadrao)
    {
        var n = grafo.Nos.Count;
        if (n == 0) return new GrafoPosicionado(grafo, [], largura, altura);
        if (n == 1) return new GrafoPosicionado(grafo, [new PosicaoDoNo(0, largura / 2, altura / 2, RaioDe(grafo.Nos[0].Grau))], largura, altura);

        var x = new double[n];
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            // Posição inicial DETERMINÍSTICA, derivada do caminho da nota. Distribuída num círculo para
            // que o grafo não comece colapsado num ponto, o que faria a repulsão explodir na 1ª iteração.
            var semente = HashEstavel(grafo.Nos[i].Caminho.Valor);
            var angulo = (semente % 10000) / 10000.0 * 2 * Math.PI;
            var raio = 0.25 + ((semente / 10000) % 1000) / 1000.0 * 0.2;
            x[i] = largura / 2 + Math.Cos(angulo) * largura * raio;
            y[i] = altura / 2 + Math.Sin(angulo) * altura * raio;
        }

        // k = distância de repouso ideal entre nós, dada a área disponível (Fruchterman-Reingold)
        var k = Math.Sqrt(largura * altura / n);
        var temperatura = largura / 10.0;
        var resfriamento = temperatura / (iteracoes + 1);

        var dx = new double[n];
        var dy = new double[n];

        for (var passo = 0; passo < iteracoes; passo++)
        {
            Array.Clear(dx);
            Array.Clear(dy);

            // repulsão: todos contra todos
            for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                var vx = x[i] - x[j];
                var vy = y[i] - y[j];
                var dist = Math.Sqrt(vx * vx + vy * vy);
                // Piso na distância: dois nós exatamente sobrepostos dariam divisão por zero e jogariam
                // ambos para o infinito. Acontece de verdade quando dois caminhos têm o mesmo hash.
                if (dist < 0.01) { dist = 0.01; vx = 0.01; vy = 0.01; }
                var forca = k * k / dist;
                dx[i] += vx / dist * forca; dy[i] += vy / dist * forca;
                dx[j] -= vx / dist * forca; dy[j] -= vy / dist * forca;
            }

            // atração: só entre nós ligados. É o que faz assunto ligado virar aglomerado visível.
            foreach (var a in grafo.Arestas)
            {
                var vx = x[a.De] - x[a.Para];
                var vy = y[a.De] - y[a.Para];
                var dist = Math.Max(0.01, Math.Sqrt(vx * vx + vy * vy));
                // peso multiplica: duas notas que se citam cinco vezes ficam mais perto que as que se
                // citam uma. O grafo passa a mostrar INTENSIDADE de relação, não só existência.
                var forca = dist * dist / k * Math.Min(a.Peso, 5);
                dx[a.De] -= vx / dist * forca; dy[a.De] -= vy / dist * forca;
                dx[a.Para] += vx / dist * forca; dy[a.Para] += vy / dist * forca;
            }

            // GRAVIDADE: cada nó é puxado de leve para o centro.
            //
            // Sem ela, um nó que não tem NENHUMA ligação só sente repulsão — e repulsão não tem
            // contrapeso, então ele é empurrado para longe até a temperatura acabar. O estrago não é o
            // nó fujão: é que o enquadramento final usa a caixa de todos os nós, então dois órfãos nos
            // cantos encolhem o aglomerado inteiro num ponto. Justamente o que o grafo servia para
            // mostrar, apagado pelo que ele servia para denunciar.
            //
            // A forma é a mesma da atração (d²/k), como se cada nó fosse ligado ao centro por um elo
            // fraco. O peso é pequeno de propósito: forte demais amontoaria tudo no meio e desfaria os
            // aglomerados.
            for (var i = 0; i < n; i++)
            {
                var gx = x[i] - largura / 2;
                var gy = y[i] - altura / 2;
                var distCentro = Math.Sqrt(gx * gx + gy * gy);
                if (distCentro < 0.01) continue;
                var forcaCentro = distCentro * distCentro / k * PesoDaGravidade;
                dx[i] -= gx / distCentro * forcaCentro;
                dy[i] -= gy / distCentro * forcaCentro;
            }

            for (var i = 0; i < n; i++)
            {
                var desloc = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                if (desloc < 1e-9) continue;
                // a temperatura limita o passo e cai a cada iteração: é o que faz o layout ASSENTAR em
                // vez de oscilar para sempre
                var limite = Math.Min(desloc, temperatura);
                x[i] += dx[i] / desloc * limite;
                y[i] += dy[i] / desloc * limite;
            }
            temperatura -= resfriamento;
        }

        return new GrafoPosicionado(grafo, Enquadrar(grafo, x, y, largura, altura), largura, altura);
    }

    /// <summary>
    /// Reescala o resultado para caber na área com margem. Sem isto, um grafo esparso ocuparia um canto
    /// e um denso vazaria da tela — a força dirigida não conhece as bordas.
    /// </summary>
    private static List<PosicaoDoNo> Enquadrar(GrafoDoVault grafo, double[] x, double[] y, double largura, double altura)
    {
        var margem = 40.0;
        double minX = x.Min(), maxX = x.Max(), minY = y.Min(), maxY = y.Max();
        var escalaX = maxX - minX < 1e-6 ? 1 : (largura - 2 * margem) / (maxX - minX);
        var escalaY = maxY - minY < 1e-6 ? 1 : (altura - 2 * margem) / (maxY - minY);
        // uma escala só nos dois eixos: escalar separadamente distorceria os ângulos e o aglomerado
        // deixaria de parecer um aglomerado
        var escala = Math.Min(escalaX, escalaY);

        var centroX = (minX + maxX) / 2;
        var centroY = (minY + maxY) / 2;

        var posicoes = new List<PosicaoDoNo>(grafo.Nos.Count);
        for (var i = 0; i < grafo.Nos.Count; i++)
        {
            posicoes.Add(new PosicaoDoNo(
                i,
                Arredondar(largura / 2 + (x[i] - centroX) * escala),
                Arredondar(altura / 2 + (y[i] - centroY) * escala),
                RaioDe(grafo.Nos[i].Grau)));
        }
        return posicoes;
    }

    /// <summary>Nó mais ligado é maior — a raiz faz crescer sem que um hub de 200 links vire um disco.</summary>
    private static double RaioDe(int grau) => Arredondar(4 + Math.Sqrt(grau) * 2.5);

    private static double Arredondar(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Hash estável entre execuções e entre máquinas.
    ///
    /// string.GetHashCode() NÃO serve: o .NET o randomiza por processo desde o Core, justamente para
    /// evitar ataques de colisão. Usá-lo aqui faria o grafo desenhar diferente a cada reinício do
    /// servidor — o defeito exato que este layout existe para não ter. FNV-1a resolve em cinco linhas.
    /// </summary>
    private static uint HashEstavel(string texto)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in texto) { hash ^= c; hash *= 16777619u; }
            return hash;
        }
    }
}
