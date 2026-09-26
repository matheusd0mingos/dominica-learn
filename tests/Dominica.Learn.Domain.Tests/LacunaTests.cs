using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// A lacuna é o formato certo para lei: "o art. ==178== define os grupos do balanço" vira cartão sem
/// inventar pergunta nem duplicar a frase.
///
/// O risco dela é o oposto do risco dos outros formatos: "==" é destaque comum de Markdown, e uma regra
/// gulosa transformaria em cartão todo trecho que alguém marcou só para reler. Metade dos testes aqui é
/// sobre o que NÃO deve virar cartão.
/// </summary>
public class LacunaTests
{
    private static readonly CaminhoNota Nota = CaminhoNota.De("Direito/Lei 6.404.md");

    private static IReadOnlyList<Cartao> Ler(string texto) => AnalisadorDeCartoes.Analisar(Nota, texto);

    [Fact]
    public void OTrechoDestacadoSomeDaFrenteEVoltaNoVerso()
    {
        var c = Assert.Single(Ler("O art. ==178== define os grupos do balanço."));

        Assert.Equal("O art. […] define os grupos do balanço.", c.Frente);
        Assert.Equal("O art. 178 define os grupos do balanço.", c.Verso);
    }

    [Fact]
    public void DuasLacunasNaMesmaLinhaViramUmCartaoSo()
    {
        // Decisão registrada: o plugin do Obsidian faria dois cartões com agendamentos separados, numa
        // marca de vários campos que este código não sabe ler de volta. Um cartão só mantém o arquivo
        // válido nos dois lados.
        var c = Assert.Single(Ler("O art. ==178== trata do ==balanço patrimonial==."));

        Assert.Equal("O art. […] trata do […].", c.Frente);
        Assert.Equal("O art. 178 trata do balanço patrimonial.", c.Verso);
    }

    [Fact]
    public void ALinhaQueEhSoODestaqueNaoEhCartao()
    {
        // A frente seria "[…]" — um cartão sem pergunta. Isso é destaque de Markdown para reler, e é o
        // uso mais comum de "==" numa nota de estudo.
        Assert.Empty(Ler("==Prescrição quinquenal=="));
    }

    [Fact]
    public void DestaqueSoltoSemFecharNaoEhCartao()
    {
        Assert.Empty(Ler("O prazo é de == cinco anos"));
    }

    [Fact]
    public void LacunaVaziaNaoEhCartao()
    {
        Assert.Empty(Ler("O art. ==  == define os grupos."));
    }

    [Fact]
    public void DentroDeBlocoDeCodigoNaoEhCartao()
    {
        // "==" aparece em comparação de igualdade em quase toda linguagem.
        Assert.Empty(Ler("```\nif (a ==b== c) {}\n```"));
    }

    [Fact]
    public void OSeparadorExplicitoTemPrioridadeSobreALacuna()
    {
        // Uma linha com "::" E "==" é um cartão explícito com destaque no meio. Quem escreveu "::"
        // declarou o que queria; transformá-la em lacuna seria desobedecer.
        var c = Assert.Single(Ler("Artigo dos grupos do balanço::o ==178=="));

        Assert.Equal("Artigo dos grupos do balanço", c.Frente);
        Assert.Contains("178", c.Verso);
    }

    [Fact]
    public void OAgendamentoEhLidoNaMesmaLinha()
    {
        // Sem isto, cada revisão recomeçaria do zero — o cartão voltaria para a fila como inédito para
        // sempre, e ninguém entenderia por quê.
        var c = Assert.Single(Ler("O art. ==178== define os grupos. <!--SR:!2026-09-01,12,250-->"));

        Assert.NotNull(c.Agendamento);
        Assert.Equal(12, c.Agendamento.IntervaloEmDias);
        Assert.DoesNotContain("SR:", c.Verso);
    }

    [Fact]
    public void ItemDeListaComLacunaFunciona()
    {
        // É como se escreve de verdade: um rol de artigos em lista, com o número destacado.
        var c = Assert.Single(Ler("- art. ==187== — a DRE"));

        Assert.StartsWith("art. […]", c.Frente);
        Assert.DoesNotContain("- ", c.Frente);
    }

    [Fact]
    public void OCartaoSabeEmQueLinhaEleEsta()
    {
        var c = Assert.Single(Ler("# Lei 6.404\n\nO art. ==178== define os grupos."));

        Assert.Equal(2, c.Linha);
    }
}
