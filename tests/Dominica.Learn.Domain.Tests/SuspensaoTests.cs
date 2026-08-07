using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// Suspender existe porque a alternativa real não é apagar — é MENTIR PARA O ALGORITMO. Sem uma saída, a
/// pessoa responde "bom" num cartão que não presta só para ele sair da frente, e ele volta em três dias.
///
/// A propriedade que estes testes guardam é que suspender GUARDA, não recomeça: o agendamento tem de
/// sobreviver intacto, senão dessuspender devolve dois anos de histórico para o dia zero.
/// </summary>
public class SuspensaoTests
{
    private static readonly CaminhoNota Nota = CaminhoNota.De("Direito/Prazos.md");

    private static IReadOnlyList<Cartao> Ler(string texto) => AnalisadorDeCartoes.Analisar(Nota, texto);

    [Fact]
    public void OCartaoSuspensoEhReconhecido()
    {
        var c = Assert.Single(Ler("Prazo::3 dias <!--SR:!2026-09-01,12,250--><!--suspenso-->"));

        Assert.True(c.Suspenso);
    }

    [Fact]
    public void OAGENDAMENTOSOBREVIVEAsuspensao()
    {
        // A PROPRIEDADE CENTRAL. Se o histórico se perdesse, suspender seria apagar com outro nome —
        // dessuspender devolveria um cartão de dois anos para o dia zero.
        var c = Assert.Single(Ler("Prazo::3 dias <!--SR:!2026-09-01,12,250--><!--suspenso-->"));

        Assert.NotNull(c.Agendamento);
        Assert.Equal(12, c.Agendamento.IntervaloEmDias);
        Assert.Equal(250, c.Agendamento.Facilidade);
    }

    [Fact]
    public void AMarcaNaoAparecEnoTextoDoCartao()
    {
        // Sem tirá-la, o comentário HTML apareceria colado na resposta, na tela de revisão.
        var c = Assert.Single(Ler("Prazo::3 dias <!--suspenso-->"));

        Assert.Equal("3 dias", c.Verso);
        Assert.DoesNotContain("suspenso", c.Verso);
    }

    [Fact]
    public void SuspenderAcrescentaAMarcaSemMexerNoResto()
    {
        var texto = "# Prazos\n\nPrazo::3 dias <!--SR:!2026-09-01,12,250-->\n\nOutra coisa.";

        var novo = Suspensao.Definir(texto, linha: 2, suspenso: true);

        Assert.Equal("Prazo::3 dias <!--SR:!2026-09-01,12,250--><!--suspenso-->", novo.Split('\n')[2]);
        Assert.Equal("# Prazos", novo.Split('\n')[0]);
        Assert.Equal("Outra coisa.", novo.Split('\n')[4]);
    }

    [Fact]
    public void DessuspenderTiraAMarcaEDevolveOCartaoParaAFila()
    {
        var texto = "Prazo::3 dias <!--SR:!2026-09-01,12,250--><!--suspenso-->";

        var novo = Suspensao.Definir(texto, linha: 0, suspenso: false);

        Assert.DoesNotContain("suspenso", novo);
        Assert.False(Assert.Single(Ler(novo)).Suspenso);
        Assert.Equal(12, Assert.Single(Ler(novo)).Agendamento!.IntervaloEmDias);
    }

    [Fact]
    public void SuspenderDuasVezesDevolveOMESMOTexto()
    {
        // A MESMA INSTÂNCIA quando nada muda, pelo mesmo motivo de EscritorDeAgendamento: gravar um
        // arquivo idêntico acordaria o vigia do vault num laço.
        var texto = "Prazo::3 dias <!--suspenso-->";

        Assert.Same(texto, Suspensao.Definir(texto, 0, suspenso: true));
    }

    [Fact]
    public void DessuspenderOQueNaoEstaSuspensoNaoMudaNada()
    {
        var texto = "Prazo::3 dias";

        Assert.Same(texto, Suspensao.Definir(texto, 0, suspenso: false));
    }

    [Fact]
    public void FinalDeLinhaWindowsEhPreservado()
    {
        // Trocar "\r\n" por "\n" marcaria a nota inteira como alterada: suspender um cartão pareceria
        // uma reescrita completa do arquivo.
        var texto = "# Prazos\r\nPrazo::3 dias\r\nFim.";

        var novo = Suspensao.Definir(texto, 1, suspenso: true);

        Assert.Contains("\r\n", novo);
        Assert.Equal("# Prazos", novo.Split("\r\n")[0]);
    }

    [Fact]
    public void CartaoDeBlocoTambemSuspende()
    {
        // No bloco a marca vai na linha da PERGUNTA — a do agendamento fica depois do verso e pode nem
        // existir ainda.
        var texto = "Modalidades<!--suspenso-->\n?\nPregão e concorrência";

        var c = Assert.Single(Ler(texto));

        Assert.True(c.Suspenso);
        Assert.Equal("Modalidades", c.Frente);
    }

    [Fact]
    public void LacunaTambemSuspende()
    {
        var c = Assert.Single(Ler("O art. ==178== define os grupos. <!--suspenso-->"));

        Assert.True(c.Suspenso);
        Assert.Equal("O art. […] define os grupos.", c.Frente);
    }

    [Fact]
    public void LinhaForaDoIntervaloNaoQuebra()
    {
        var texto = "Prazo::3 dias";

        Assert.Same(texto, Suspensao.Definir(texto, 99, suspenso: true));
        Assert.Same(texto, Suspensao.Definir(texto, -1, suspenso: true));
    }
}
