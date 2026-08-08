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

    /// <summary>
    /// Quanto notas da MESMA matéria se atraem, na ausência de ligação entre elas. Bem abaixo do peso de
    /// uma ligação (que é 1) — a pasta insinua o aglomerado, a ligação é que o forma.
    /// </summary>
    private const double CoesaoDeMateria = 0.06;

    public static GrafoPosicionado Calcular(
        GrafoDoVault grafo, double largura = 800, double altura = 600, int iteracoes = IteracoesPadrao) =>
        new(grafo,
            Posicionar(
                [.. grafo.Nos.Select(no => no.Caminho.Valor)],
                [.. grafo.Nos.Select(no => no.Grau)],
                IndicesDeMateria(grafo),
                grafo.Arestas,
                largura, altura, iteracoes),
            largura, altura);

    /// <summary>
    /// O LAYOUT, POR ÍNDICE — sem saber o que os nós são.
    ///
    /// Recebe quatro arrays paralelos e devolve posições numeradas. Não há nota, caminho nem matéria
    /// nesta assinatura, e é isso que permite desenhar mais de um tipo de mapa com o mesmo motor: o
    /// grafo de notas passa caminho/grau/matéria; o mapa de etiquetas passa valor/vizinhas/-1.
    ///
    /// A ALTERNATIVA ERA FABRICAR CaminhoNota FALSO para cada etiqueta, e ela é pior de um jeito que só
    /// aparece anos depois: quem abrisse o depurador veria "notas" que não existem em disco nenhum e
    /// passaria uma tarde entendendo por quê. Um parâmetro a mais aqui custa uma linha.
    /// </summary>
    /// <param name="sementes">Texto estável por nó — dele sai a posição INICIAL, e é o que faz o mesmo
    /// conjunto sair sempre com o mesmo desenho.</param>
    /// <param name="graus">Quantas ligações cada nó tem. Vira o raio do ponto.</param>
    /// <param name="grupos">A que aglomerado o nó pertence, para a coesão fraca. -1 = nenhum, e
    /// "nenhum" NÃO é um grupo em comum.</param>
    public static IReadOnlyList<PosicaoDoNo> Posicionar(
        string[] sementes, int[] graus, int[] grupos, IReadOnlyList<ArestaDoGrafo> arestas,
        double largura = 800, double altura = 600, int iteracoes = IteracoesPadrao)
    {
        var n = sementes.Length;
        if (n == 0) return [];
        if (n == 1) return [new PosicaoDoNo(0, largura / 2, altura / 2, RaioDe(graus[0]))];

        var x = new double[n];
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            // Posição inicial DETERMINÍSTICA, derivada do caminho da nota. Distribuída num círculo para
            // que o grafo não comece colapsado num ponto, o que faria a repulsão explodir na 1ª iteração.
            var semente = HashEstavel(sementes[i]);
            var angulo = (semente % 10000) / 10000.0 * 2 * Math.PI;
            var raio = 0.25 + ((semente / 10000) % 1000) / 1000.0 * 0.2;
            x[i] = largura / 2 + Math.Cos(angulo) * largura * raio;
            y[i] = altura / 2 + Math.Sin(angulo) * altura * raio;
        }

        // k = distância de repouso ideal entre nós, dada a área disponível (Fruchterman-Reingold)
        var k = Math.Sqrt(largura * altura / n);
        var temperatura = largura / 10.0;
        var resfriamento = temperatura / (iteracoes + 1);

        // Matéria como índice inteiro, calculado UMA vez: comparar por número no laço O(n²) em vez de
        // comparar registros a cada uma das ~90 000 combinações por iteração, vezes 300 iterações.
        // -1 = sem matéria, e "sem matéria" NÃO é uma matéria em comum: notas soltas na raiz não têm
        // nada a ver umas com as outras só por estarem soltas.
        var materias = grupos;

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

                // COESÃO POR MATÉRIA: notas da mesma matéria se repelem MENOS.
                //
                // Existe porque um vault jovem quase não tem ligações — nos primeiros meses você escreve
                // muito e liga pouco. Sem isto, o grafo de quem começou a estudar há três semanas é uma
                // nuvem uniforme que não diz nada, e a pergunta "como está Direito comparado a Português?"
                // não tem resposta na tela. A pasta, essa, existe desde o primeiro arquivo.
                //
                // O sinal subtraído do termo de repulsão é literalmente uma atração fraca — e ela é fraca
                // de propósito: forte demais desenharia as PASTAS, não as ideias, e o grafo viraria um
                // explorador de arquivos redondo. Quem manda no desenho continua sendo a ligação.
                if (materias is not null && materias[i] == materias[j] && materias[i] >= 0)
                    forca -= dist * dist / k * CoesaoDeMateria;

                dx[i] += vx / dist * forca; dy[i] += vy / dist * forca;
                dx[j] -= vx / dist * forca; dy[j] -= vy / dist * forca;
            }

            // atração: só entre nós ligados. É o que faz assunto ligado virar aglomerado visível.
            foreach (var a in arestas)
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

        return Enquadrar(graus, x, y, largura, altura);
    }

    /// <summary>
    /// Reescala o resultado para caber na área com margem. Sem isto, um grafo esparso ocuparia um canto
    /// e um denso vazaria da tela — a força dirigida não conhece as bordas.
    /// </summary>
    private static List<PosicaoDoNo> Enquadrar(int[] graus, double[] x, double[] y, double largura, double altura)
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

        var posicoes = new List<PosicaoDoNo>(graus.Length);
        for (var i = 0; i < graus.Length; i++)
        {
            posicoes.Add(new PosicaoDoNo(
                i,
                Arredondar(largura / 2 + (x[i] - centroX) * escala),
                Arredondar(altura / 2 + (y[i] - centroY) * escala),
                RaioDe(graus[i])));
        }
        return posicoes;
    }

    /// <summary>Matéria de cada nó como inteiro; -1 para quem está na raiz do vault.</summary>
    private static int[] IndicesDeMateria(GrafoDoVault grafo)
    {
        var mapa = new Dictionary<Vault.Materia, int>();
        var indices = new int[grafo.Nos.Count];
        for (var i = 0; i < grafo.Nos.Count; i++)
        {
            var m = grafo.Nos[i].Materia;
            if (!m.Existe) { indices[i] = -1; continue; }
            if (!mapa.TryGetValue(m, out var idx)) mapa[m] = idx = mapa.Count;
            indices[i] = idx;
        }
        return indices;
    }

    /// <summary>Nó mais ligado é maior — a raiz faz crescer sem que um hub de 200 links vire um disco.</summary>
    /// <summary>
    /// O raio do ponto, pelo grau — quantas ligações a nota tem.
    ///
    /// OS NÚMEROS SUBIRAM depois de ver o grafo de um vault pequeno: com base 4, uma nota recém-ligada
    /// e uma nota órfã ficavam do mesmo tamanho aos olhos, e quem acabava de criar a primeira ligação
    /// da vida via a tela igualzinha à de antes. Um recurso que não dá retorno visível é um recurso que
    /// a pessoa conclui que não funcionou — e foi exatamente essa a conclusão que chegou aqui.
    ///
    /// Base 6, passo 3.5: grau 0 fica em 6, grau 1 em 9.5, grau 4 em 13. A diferença entre "ninguém
    /// aponta para isto" e "já tem uma ligação" passou a ser vista sem procurar.
    ///
    /// CONTINUA RAIZ QUADRADA, e não linear: num vault de anos a nota-índice de uma matéria chega a
    /// dezenas de ligações, e crescimento linear a transformaria num disco cobrindo o resto do mapa.
    /// </summary>
    private static double RaioDe(int grau) => Arredondar(6 + Math.Sqrt(grau) * 3.5);

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
