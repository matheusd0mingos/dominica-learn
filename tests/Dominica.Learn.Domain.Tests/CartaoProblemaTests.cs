using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O cartão que se erra sempre.
///
/// Meia dúzia deles come a sessão inteira todo dia — errar devolve o cartão à fila de hoje, que é o
/// certo, então um cartão que não entra na cabeça reaparece para sempre. O estrago não é o tempo: é a
/// conclusão que a pessoa tira disso, que é "eu tenho memória ruim".
/// </summary>
public class CartaoProblemaTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 7);
    private static CaminhoNota N(string nome = "Direito/Licitações.md") => CaminhoNota.De(nome);

    private static Cartao C(int? facilidade, bool suspenso = false, string nota = "Direito/Licitações.md", int linha = 0) =>
        new(N(nota), $"P{linha}", "R", linha, 1,
            facilidade is null ? null : new Agendamento(Hoje, 10, facilidade.Value), suspenso);

    // —— QUEM É PROBLEMA ————————————————————————————————————————————————————————————

    [Fact]
    public void CartaoINEDITONuncaEhProblema()
    {
        // Ele ainda não teve chance de ser um. Acusá-lo seria confundir "não sei" com "não aprendo".
        Assert.False(CartaoProblema.Eh(C(facilidade: null)));
        Assert.Null(CartaoProblema.Diagnostico(C(facilidade: null)));
    }

    [Fact]
    public void CartaoNOVOEmDia_naoEhProblema()
    {
        Assert.False(CartaoProblema.Eh(C(Agendamento.FacilidadePadrao)));
    }

    [Fact]
    public void UmDiaRuimNaoBASTA()
    {
        // Um tropeço (−20) e dois (−40) não acusam ninguém. O aviso tem de chegar depois de um padrão,
        // não depois de uma tarde cansada.
        Assert.False(CartaoProblema.Eh(C(230)));
        Assert.False(CartaoProblema.Eh(C(210)));
        Assert.False(CartaoProblema.Eh(C(190)));
    }

    [Fact]
    public void QUATROTROPECOSJaEhProblema()
    {
        // 250 − 4×20 = 170. É o limiar, e ele é inclusivo.
        Assert.True(CartaoProblema.Eh(C(CartaoProblema.FacilidadeDeProblema)));
        Assert.True(CartaoProblema.Eh(C(150)));
    }

    [Fact]
    public void OPISOEhOCasoGRAVE()
    {
        Assert.True(CartaoProblema.NoPiso(C(Agendamento.FacilidadeMinima)));
        Assert.False(CartaoProblema.NoPiso(C(150)));
    }

    // —— O QUE SE DIZ ————————————————————————————————————————————————————————————————

    [Fact]
    public void ODiagnosticoTerminaCOMOQUEFAZER()
    {
        // Um aviso que só diagnostica deixa a pessoa onde ela estava, agora também culpada. A frase tem
        // de sair com uma saída: reescrever ou suspender.
        var texto = CartaoProblema.Diagnostico(C(Agendamento.FacilidadeMinima))!;

        Assert.Contains("não a sua memória", texto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reescrev", texto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("suspend", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ODiagnosticoDizAFACILIDADEDEVERDADE()
    {
        // O número na tela é o que se sabe, não um palpite: o formato do plugin guarda a facilidade, e
        // não o número de falhas. Contar erros a partir daí seria apresentar chute como dado.
        Assert.Contains("1.50×", CartaoProblema.Diagnostico(C(150))!.Replace(',', '.'));
    }

    [Fact]
    public void OPisoEOAvisoLEVEDizemCoisasDIFERENTES()
    {
        Assert.NotEqual(CartaoProblema.Diagnostico(C(150)), CartaoProblema.Diagnostico(C(130)));
    }

    // —— A LISTA ————————————————————————————————————————————————————————————————————

    [Fact]
    public void OPIORVemPrimeiro()
    {
        var lista = CartaoProblema.Ordenados([C(170, linha: 0), C(130, linha: 1), C(150, linha: 2)]);

        Assert.Equal([130, 150, 170], lista.Select(c => c.Agendamento!.Facilidade));
    }

    [Fact]
    public void SUSPENSOFicaDeFORA()
    {
        // Ele já foi tirado da frente de propósito. Listá-lo seria pedir uma decisão que já foi tomada.
        var lista = CartaoProblema.Ordenados([C(130, suspenso: true), C(150)]);

        Assert.Equal([150], lista.Select(c => c.Agendamento!.Facilidade));
    }

    [Fact]
    public void SemProblemaNENHUMAListaEhVAZIA()
    {
        Assert.Empty(CartaoProblema.Ordenados([C(250), C(230), C(null)]));
        Assert.Empty(CartaoProblema.Ordenados([]));
    }

    [Fact]
    public void AOrdemEhESTAVEL()
    {
        Cartao[] cartoes = [C(150, nota: "B/n.md", linha: 1), C(150, nota: "A/n.md", linha: 0), C(150, nota: "A/n.md", linha: 3)];

        Assert.Equal(
            CartaoProblema.Ordenados(cartoes).Select(c => $"{c.Nota.Valor}:{c.Linha}"),
            CartaoProblema.Ordenados(cartoes).Select(c => $"{c.Nota.Valor}:{c.Linha}"));
    }

    // —— A CONSISTÊNCIA COM O AGENDADOR ————————————————————————————————————————————

    [Fact]
    public void QuatroERROSDeVerdadeCHEGAMNoLimiar()
    {
        // O limiar não é um número solto: ele é o resultado de rodar o agendador de verdade quatro
        // vezes. Se a curva do SM-2 mudar, é este teste que avisa que o limiar deixou de significar
        // "quatro tropeços".
        var a = Agendamento.Novo(Hoje);
        for (var i = 0; i < 4; i++) a = AgendadorSM2.Proximo(a, Resposta.Errei, Hoje);

        Assert.Equal(CartaoProblema.FacilidadeDeProblema, a.Facilidade);
        Assert.True(CartaoProblema.Eh(C(a.Facilidade)));
    }
}
