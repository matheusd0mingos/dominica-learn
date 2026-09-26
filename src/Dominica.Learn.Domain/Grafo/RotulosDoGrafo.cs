namespace Dominica.Learn.Domain.Grafo;

/// <summary>Um nó candidato a receber rótulo, com o peso que decide quem passa na frente.</summary>
/// <param name="Indice">Posição do nó na lista de posições.</param>
/// <param name="Texto">O rótulo já encurtado — o que vai de fato ser desenhado.</param>
/// <param name="Prioridade">Maior ganha a vaga. Ver <see cref="RotulosDoGrafo"/>.</param>
public sealed record CandidatoARotulo(int Indice, string Texto, int Prioridade);

/// <summary>De que lado do ponto o rótulo coube.</summary>
public enum LadoDoRotulo
{
    /// <summary>O padrão: é onde o olho procura a legenda de um ponto.</summary>
    Abaixo,

    /// <summary>A alternativa, quando embaixo está ocupado. O nome continua colado no dono.</summary>
    Acima,
}

/// <summary>
/// QUAIS RÓTULOS CABEM NO DESENHO — e quais têm de ficar de fora.
///
/// O MECANISMO ANTIGO CONTAVA NÓS, E CONTAR NÓS NÃO É MEDIR ESPAÇO. A regra era: até 25 notas, rotule
/// todas; até 80, só as de grau 3; acima disso, grau 8. Medido no navegador com 36 notas: 27 rótulos
/// desenhados e NOVE PARES se sobrepondo — porque o que enche a tela não é a quantidade de pontos, é o
/// COMPRIMENTO DOS NOMES perto do espaço entre eles. "CPC 48 — Instrumentos…" ocupa dez vezes a largura
/// do ponto que ele nomeia. Dois pontos podem estar confortavelmente separados e os nomes deles, não.
///
/// E o efeito de contar nós é perverso no lugar errado: o grafo de UMA matéria tem poucos nós, então a
/// regra antiga rotulava todos — justo o recorte mais apertado, onde as notas estão amontoadas porque
/// se citam. Quanto mais coeso o assunto, mais ilegível ficava.
///
/// —— O QUE ESTA CLASSE FAZ ————————————————————————————————————————————————————————————
///
/// Coloca os rótulos EM ORDEM DE IMPORTÂNCIA e pula quem colidiria com um já colocado. É o algoritmo
/// guloso clássico de rotulagem cartográfica, e ele tem a propriedade que este produto precisa: o que
/// importa mais aparece sempre, e o que fica de fora é sempre o menos importante — nunca "o que calhou".
///
/// DETERMINÍSTICO, como o resto do desenho: mesma entrada, mesmos rótulos, sempre. Um grafo que troca
/// quais nomes mostra a cada abertura destrói a memória espacial, que é a razão de ele existir.
///
/// PURO E TESTÁVEL, como o layout. Medir texto no navegador para depois decidir exigiria uma ida e volta
/// pelo circuito antes de desenhar — e tornaria a decisão inauditável.
///
/// —— O QUE ELE NÃO FAZ, DE PROPÓSITO ——————————————————————————————————————————————————
///
/// NÃO EMPURRA O RÓTULO PARA UM CANTO LIVRE. Rotulagem com deslocamento fica bonita e mente: o nome
/// passa a flutuar longe do ponto, e num grafo denso ninguém sabe mais qual nome é de qual bolinha.
/// Duas posições coladas no dono — embaixo, ou em cima —, e fora disso o nome não aparece.
///
/// NÃO DIMINUI A FONTE para caber mais. Rótulo ilegível não é informação — é sujeira com boa intenção.
/// </summary>
public static class RotulosDoGrafo
{
    /// <summary>
    /// Largura de um caractere, em múltiplos do tamanho da fonte.
    ///
    /// MEDIDO, NÃO CHUTADO. A primeira versão usou 0,52 por estimativa e deixou passar sobreposição.
    /// Medindo getBBox nos 29 rótulos de um vault real, com a fonte de interface: mediana 0,569,
    /// percentil 90 em 0,602, e nenhum acima de 0,62 fora de rótulos de uma letra só.
    ///
    /// ERRAR PARA MAIS É O LADO SEGURO: o pior resultado é um nome a menos na tela; errar para menos é
    /// a sujeira que esta classe existe para evitar. Por isso 0,60 (acima da mediana) e não a média.
    /// </summary>
    private const double LarguraPorCaractere = 0.60;

    /// <summary>
    /// A folga lateral da caixa do texto, em múltiplos da fonte.
    ///
    /// Rótulo curto tem proporcionalmente MAIS largura por caractere — as bordas laterais da primeira e
    /// da última letra não encolhem junto. Medido: um rótulo de uma letra chega a 0,82 por caractere.
    /// Esta parcela fixa cobre isso sem inflar os longos.
    /// </summary>
    private const double BordasDoTexto = 0.5;

    /// <summary>Altura da caixa do rótulo, em múltiplos da fonte. Uma linha, com a folga da entrelinha.</summary>
    private const double AlturaDaLinha = 1.25;

    /// <summary>
    /// Folga entre duas caixas. Dois nomes que encostam sem sobrepor ainda se leem como um só — a
    /// separação precisa ser visível, não apenas existir.
    /// </summary>
    private const double Folga = 2.0;

    /// <summary>Distância do centro do ponto até a linha de base do rótulo, além do raio.</summary>
    public const double AbaixoDoPonto = 11;

    /// <summary>
    /// Os nós que recebem rótulo, e de que lado do ponto cada um coube.
    /// </summary>
    /// <param name="posicoes">Onde cada nó está e de que tamanho — a saída do <see cref="LayoutDeForca"/>.</param>
    /// <param name="candidatos">Quem quer rótulo, com o texto já encurtado e a prioridade.</param>
    /// <param name="tamanhoDaFonte">Em unidades do viewBox, o mesmo que vai no atributo do SVG.</param>
    public static IReadOnlyDictionary<int, LadoDoRotulo> Escolher(
        IReadOnlyList<PosicaoDoNo> posicoes,
        IReadOnlyList<CandidatoARotulo> candidatos,
        double tamanhoDaFonte = 10)
    {
        ArgumentNullException.ThrowIfNull(posicoes);
        ArgumentNullException.ThrowIfNull(candidatos);

        var porIndice = posicoes.ToDictionary(p => p.Indice);
        var colocados = new List<(double X1, double Y1, double X2, double Y2)>();
        var escolhidos = new Dictionary<int, LadoDoRotulo>();

        // OS PONTOS TAMBÉM SÃO OBSTÁCULO, e esta regra veio de olhar a tela depois de a primeira versão
        // já estar "pronta": sem rótulo batendo em rótulo, o aglomerado denso continuava ilegível porque
        // os nomes atravessavam as BOLINHAS vizinhas — "CPC 06 — Arrendamentos" cortado ao meio por um
        // ponto laranja. Medir só texto contra texto declara vitória cedo demais.
        //
        // CONTRA O CÍRCULO, e não contra a caixa dele: com a caixa, um rótulo que passa raspando pelo
        // CANTO — onde não há tinta nenhuma — era recusado. Num aglomerado, essa diferença é a diferença
        // entre onze pontos anônimos e onze pontos com nome.
        var pontos = posicoes.ToDictionary(p => p.Indice, p => p);

        // ORDEM: prioridade primeiro; empatou, o índice — que é estável. Sem o desempate estável, dois
        // nós de mesma importância trocariam de vaga entre uma abertura e outra.
        var fila = candidatos
            .Where(c => c.Texto.Length > 0 && porIndice.ContainsKey(c.Indice))
            .OrderByDescending(c => c.Prioridade)
            .ThenBy(c => c.Indice);

        foreach (var candidato in fila)
        {
            var posicao = porIndice[candidato.Indice];

            // DUAS POSIÇÕES, e só duas: embaixo primeiro, em cima se embaixo não couber.
            //
            // Embaixo é o padrão porque é onde o olho procura a legenda de um ponto. Em cima é a única
            // alternativa que mantém o nome COLADO no dono — e num aglomerado ela salva metade dos
            // rótulos, porque a vizinhança que aperta por baixo raramente aperta por cima também.
            //
            // Quatro posições (à esquerda, à direita) seria o livro-texto de cartografia e está ERRADO
            // aqui: um nome ao lado, num grafo denso, cola visualmente no ponto vizinho, e aí o mapa
            // passa a mentir sobre qual nome é de quem. Rótulo ambíguo é pior que rótulo ausente.
            var lado = LadoDoRotulo.Abaixo;
            var caixa = CaixaDo(posicao, candidato.Texto, tamanhoDaFonte, lado);
            if (Ocupado(caixa, candidato.Indice, colocados, pontos))
            {
                lado = LadoDoRotulo.Acima;
                caixa = CaixaDo(posicao, candidato.Texto, tamanhoDaFonte, lado);
                if (Ocupado(caixa, candidato.Indice, colocados, pontos)) continue;
            }

            colocados.Add(caixa);
            escolhidos.Add(candidato.Indice, lado);
        }

        return escolhidos;
    }

    private static bool Ocupado(
        (double X1, double Y1, double X2, double Y2) caixa,
        int dono,
        List<(double X1, double Y1, double X2, double Y2)> colocados,
        Dictionary<int, PosicaoDoNo> pontos) =>
        colocados.Any(c => Batem(c, caixa))
        // O PRÓPRIO PONTO NÃO CONTA: o rótulo nasce colado nele, e encostar no dono é o desenho
        // pretendido. Contá-lo faria todo nó grande perder o nome.
        || pontos.Any(p => p.Key != dono && Encosta(p.Value, caixa));

    /// <summary>A caixa que o rótulo ocupa. Centrado no ponto (text-anchor="middle"), colado nele.</summary>
    private static (double X1, double Y1, double X2, double Y2) CaixaDo(
        PosicaoDoNo posicao, string texto, double fonte, LadoDoRotulo lado)
    {
        var largura = fonte * (texto.Length * LarguraPorCaractere + BordasDoTexto) + Folga;
        var altura = fonte * AlturaDaLinha + Folga;

        // Em cima, a linha de base fica ACIMA do topo do ponto — e a caixa sobe uma altura inteira,
        // porque o texto cresce para cima a partir da linha de base.
        var topo = lado == LadoDoRotulo.Acima
            ? posicao.Y - posicao.Raio - AbaixoDoPonto - altura + fonte
            : posicao.Y + posicao.Raio + AbaixoDoPonto - fonte;

        return (posicao.X - largura / 2, topo, posicao.X + largura / 2, topo + altura);
    }

    /// <summary>
    /// Onde desenhar a linha de base do rótulo. Existe para que a TELA não recalcule esta conta — dois
    /// lugares decidindo a mesma coordenada é um deles ficando para trás.
    /// </summary>
    public static double LinhaDeBase(PosicaoDoNo posicao, LadoDoRotulo lado) =>
        lado == LadoDoRotulo.Acima
            ? posicao.Y - posicao.Raio - AbaixoDoPonto
            : posicao.Y + posicao.Raio + AbaixoDoPonto;

    private static bool Batem(
        (double X1, double Y1, double X2, double Y2) a, (double X1, double Y1, double X2, double Y2) b) =>
        a.X1 < b.X2 && a.X2 > b.X1 && a.Y1 < b.Y2 && a.Y2 > b.Y1;

    /// <summary>
    /// O CÍRCULO encosta na caixa? Distância do centro ao ponto mais próximo do retângulo — a conta
    /// clássica, e ela é o que separa "passou por cima da bolinha" de "passou raspando pelo canto vazio".
    /// </summary>
    private static bool Encosta(PosicaoDoNo ponto, (double X1, double Y1, double X2, double Y2) caixa)
    {
        var maisPertoX = Math.Clamp(ponto.X, caixa.X1, caixa.X2);
        var maisPertoY = Math.Clamp(ponto.Y, caixa.Y1, caixa.Y2);
        var dx = ponto.X - maisPertoX;
        var dy = ponto.Y - maisPertoY;
        return dx * dx + dy * dy < ponto.Raio * ponto.Raio;
    }
}
