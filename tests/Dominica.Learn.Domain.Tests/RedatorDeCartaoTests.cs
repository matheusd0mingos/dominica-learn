using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// A prova que importa aqui é de IDA E VOLTA: o que o redator escreve, o analisador tem de ler de volta
/// como o mesmo cartão. Duas classes que discordam sobre o formato produzem cartões que somem da fila
/// sem erro nenhum — a pessoa criou, viu na nota, e no dia da revisão ele não está lá.
/// </summary>
public class RedatorDeCartaoTests
{
    private static readonly CaminhoNota Nota = CaminhoNota.De("Direito/Prazos.md");

    private static string Escrever(string frente, string verso, bool duplo = false)
    {
        Assert.True(RedatorDeCartao.TentarEscrever(frente, verso, duplo, out var texto, out var problema),
            $"esperava conseguir escrever, mas veio {problema}");
        return texto!;
    }

    private static Cartao Ler(string texto) => Assert.Single(AnalisadorDeCartoes.Analisar(Nota, texto));

    [Fact]
    public void UmaLinhaVoltaComoOMesmoCartao()
    {
        var cartao = Ler(Escrever("Prazo da impugnação", "3 dias úteis"));

        Assert.Equal("Prazo da impugnação", cartao.Frente);
        Assert.Equal("3 dias úteis", cartao.Verso);
    }

    [Fact]
    public void RespostaDeVariasLinhasViraCartaoDeBloco()
    {
        // Escrever isto em uma linha produziria um cartão truncado, e o defeito só apareceria no dia da
        // revisão, com a resposta pela metade.
        var texto = Escrever("Modalidades", "Pregão\nConcorrência\nConcurso");

        Assert.Contains("\n?\n", texto);
        var cartao = Ler(texto);
        Assert.Equal("Modalidades", cartao.Frente);
        Assert.Contains("Concorrência", cartao.Verso);
    }

    [Fact]
    public void NosDoisSentidosUsaOSeparadorTriplo()
    {
        Assert.Contains(":::", Escrever("Habeas corpus", "remédio contra prisão ilegal", duplo: true));
    }

    [Fact]
    public void NosDoisSentidosEmBlocoUsaDuasInterrogacoes()
    {
        var texto = Escrever("Modalidades", "Pregão\nConcorrência", duplo: true);

        Assert.Contains("\n??\n", texto);
        Assert.Equal("Modalidades", Ler(texto).Frente);
    }

    [Fact]
    public void EspacoEmVoltaEhAparado()
    {
        // Quem digita num campo deixa espaço sobrando o tempo todo, e ele apareceria dentro do cartão.
        Assert.Equal("Prazo::3 dias\n", Escrever("  Prazo  ", "  3 dias  "));
    }

    [Fact]
    public void PerguntaComSeparadorEhRecusada()
    {
        // O analisador corta no PRIMEIRO "::". Deixar passar geraria um cartão que já nasce lendo ao
        // contrário — parte da resposta viraria pergunta.
        Assert.False(RedatorDeCartao.TentarEscrever("Prazo::x", "3 dias", false, out var texto, out var p));

        Assert.Null(texto);
        Assert.Equal(ProblemaDoCartao.FrenteTemSeparador, p);
    }

    [Fact]
    public void LadoVazioEhRecusado()
    {
        Assert.False(RedatorDeCartao.TentarEscrever("  ", "3 dias", false, out _, out var p1));
        Assert.Equal(ProblemaDoCartao.FrenteVazia, p1);

        Assert.False(RedatorDeCartao.TentarEscrever("Prazo", null, false, out _, out var p2));
        Assert.Equal(ProblemaDoCartao.VersoVazio, p2);
    }

    [Fact]
    public void OTextoTerminaEmQuebraDeLinha()
    {
        // Inserido no cursor, sem a quebra o cartão grudaria na frase seguinte e deixaria de ser cartão.
        Assert.EndsWith("\n", Escrever("Prazo", "3 dias"));
    }

    [Fact]
    public void RespostaComSeparadorNaoAtrapalha()
    {
        // "::" na RESPOSTA é inofensivo: o corte é no primeiro, que é o que o redator escreveu.
        var cartao = Ler(Escrever("Sintaxe do cartão", "escreve-se assim: a::b"));

        Assert.Equal("Sintaxe do cartão", cartao.Frente);
    }

    [Fact]
    public void DoisPontosColadoNoSeparadorForcaOBloco()
    {
        // "Prazo" + ": 3 dias" viraria "Prazo:::3 dias" — a marca de MÃO DUPLA, com o ":" comido. O
        // cartão inverso apareceria na revisão semanas depois, sem explicação.
        var texto = Escrever("Prazo", ": 3 dias");

        Assert.DoesNotContain(":::", texto);
        Assert.Contains("\n?\n", texto);

        var cartao = Ler(texto);
        Assert.Equal("Prazo", cartao.Frente);
        Assert.Equal(": 3 dias", cartao.Verso);
    }

    [Fact]
    public void DoisPontosNoFimDaPerguntaTambemForcaOBloco()
    {
        var texto = Escrever("Prazos:", "3 dias");

        Assert.DoesNotContain(":::", texto);
        Assert.Equal("Prazos:", Ler(texto).Frente);
    }
}
