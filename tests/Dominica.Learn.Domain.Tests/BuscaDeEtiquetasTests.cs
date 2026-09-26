using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O autocompletar de "#" existe para o vault não se despedaçar em sinônimos. Um erro aqui não quebra
/// nada — só faz a lista não oferecer a etiqueta certa, e aí a pessoa digita a variação nova, que é
/// exatamente o que se queria evitar.
/// </summary>
public class BuscaDeEtiquetasTests
{
    private static (Etiqueta, int) E(string valor, int notas = 1) =>
        (Etiqueta.TentarCriar(valor)!, notas);

    private static IReadOnlyList<string> Valores(IEnumerable<AcertoDeEtiqueta> r) =>
        r.Select(a => a.Etiqueta.Valor).ToList();

    // —— CASAR ——————————————————————————————————————————————————————————————————————

    [Fact]
    public void CasaPELOCOMECODEQUALQUERSEGMENTO()
    {
        // O TESTE QUE JUSTIFICA A CLASSE. Quem digita "trib" está pensando em tributário; a etiqueta
        // é "direito/tributário". Exigir "direito/" antes seria exigir de cor a hierarquia que a
        // lista existe para lembrar.
        var r = BuscaDeEtiquetas.Ordenar("trib", [E("direito/tributário"), E("português")]);

        Assert.Equal(["direito/tributário"], Valores(r));
    }

    [Fact]
    public void OQueCASADEFORAVemAntesDoQueCasaDeDentro()
    {
        var r = BuscaDeEtiquetas.Ordenar("trib", [E("direito/tributário", 50), E("tributário", 1)]);

        Assert.Equal(["tributário", "direito/tributário"], Valores(r));
    }

    [Fact]
    public void ACENTONaoAtrapalha()
    {
        // Parar para lembrar se foi "tributário" ou "tributario" é o custo que faz desistir da lista.
        var r = BuscaDeEtiquetas.Ordenar("tributario", [E("direito/tributário")]);

        Assert.Single(r);
    }

    [Fact]
    public void CAIXANaoAtrapalha()
    {
        var r = BuscaDeEtiquetas.Ordenar("PEGA", [E("pegadinha")]);

        Assert.Single(r);
    }

    [Fact]
    public void OCerquilhaDigitadoEhIgnorado()
    {
        // O JavaScript manda o termo já sem o "#", mas quem chama de outro lugar não precisa saber disso.
        Assert.Single(BuscaDeEtiquetas.Ordenar("#pega", [E("pegadinha")]));
    }

    [Fact]
    public void NoMEIOTambemCasa_masValePOUCO()
    {
        // Quem lembra do fim e não do começo continua achando; e isso não pode empurrar para baixo quem
        // casa pelo começo.
        var r = BuscaDeEtiquetas.Ordenar("din", [E("pegadinha", 99), E("dinheiro", 1)]);

        Assert.Equal(["dinheiro", "pegadinha"], Valores(r));
    }

    [Fact]
    public void QuemNaoCasaFICADEFORA()
    {
        Assert.Empty(BuscaDeEtiquetas.Ordenar("xyz", [E("pegadinha"), E("direito/tributário")]));
    }

    // —— A ORDEM ————————————————————————————————————————————————————————————————————

    [Fact]
    public void ENTREIGUAISVenceAMAISUSADA()
    {
        // O ponto da classe, do lado da ordenação: "#pegadinha" com 40 notas e "#pegadinhas" com 1 são
        // a etiqueta e o engano dela. A de 40 tem de vir primeiro, ou o engano se repete.
        var r = BuscaDeEtiquetas.Ordenar("pegadinha", [E("pegadinhas", 1), E("pegadinha", 40)]);

        Assert.Equal(["pegadinha", "pegadinhas"], Valores(r));
    }

    [Fact]
    public void OEXATOVemPrimeiroAindaQueMenosUsado()
    {
        // Digitou a etiqueta inteira: não há dúvida sobre o que ele quer, nem com uso de 1 contra 99.
        var r = BuscaDeEtiquetas.Ordenar("norma", [E("norma/cpc", 99), E("norma", 1)]);

        Assert.Equal("norma", r[0].Etiqueta.Valor);
    }

    [Fact]
    public void ContagemNaoAtravessaDoisDegraus()
    {
        // Casar pelo começo do valor tem de ganhar de casar no meio, por mais usada que a outra seja —
        // senão a ordenação vira "as mais usadas do vault", que não responde ao que foi digitado.
        var r = BuscaDeEtiquetas.Ordenar("dir", [E("lei/direta", 500), E("direito", 1)]);

        Assert.Equal("direito", r[0].Etiqueta.Valor);
    }

    [Fact]
    public void TermoVAZIODevolveASMAISUSADAS()
    {
        // Quem acabou de digitar "#" não tem assunto em mente; o vocabulário que ele mais repete é o
        // melhor palpite. Alfabético ali seria a informação menos útil possível.
        var r = BuscaDeEtiquetas.Ordenar("", [E("rara", 1), E("comum", 80), E("media", 12)]);

        Assert.Equal(["comum", "media", "rara"], Valores(r));
    }

    [Fact]
    public void AOrdemEhESTAVEL()
    {
        // A escolha é feita com a seta, sem reler a lista. Duas chamadas iguais têm de devolver a mesma
        // ordem, ou a terceira seta escolhe outra coisa.
        var candidatas = new[] { E("a/um", 3), E("a/dois", 3), E("a/tres", 3) };

        Assert.Equal(Valores(BuscaDeEtiquetas.Ordenar("a", candidatas)),
                     Valores(BuscaDeEtiquetas.Ordenar("a", candidatas)));
    }

    [Fact]
    public void ListaVAZIANaoQuebra()
    {
        Assert.Empty(BuscaDeEtiquetas.Ordenar("qualquer", []));
        Assert.Empty(BuscaDeEtiquetas.Ordenar("", []));
    }
}
