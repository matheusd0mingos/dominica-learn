using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Xunit;

namespace Dominica.Learn.Domain.Tests;

public class CodigoDeTurmaTests
{
    /// <summary>
    /// O CÓDIGO É DITADO POR VOZ e copiado de um quadro. Os pares que se confundem — 0/O, 1/I/L —
    /// ficam fora do alfabeto, senão "o código não funciona" e quem não entra na turma não reclama:
    /// desiste.
    /// </summary>
    [Fact]
    public void Sorteado_nunca_usa_caractere_ambiguo()
    {
        for (var i = 0; i < 500; i++)
        {
            var c = CodigoDeTurma.Sortear();
            Assert.Equal(CodigoDeTurma.Tamanho, c.Valor.Length);
            Assert.DoesNotContain(c.Valor, ch => ch is '0' or 'O' or '1' or 'I' or 'L');
        }
    }

    /// <summary>Dois sorteios seguidos iguais seriam sinal de gerador quebrado, não de azar.</summary>
    [Fact]
    public void Sorteados_nao_se_repetem_em_lote()
    {
        var vistos = new HashSet<string>();
        for (var i = 0; i < 500; i++) vistos.Add(CodigoDeTurma.Sortear().Valor);
        Assert.True(vistos.Count > 490, $"só {vistos.Count} códigos distintos em 500 sorteios");
    }

    /// <summary>
    /// ACEITA O QUE A PESSOA DIGITA. Quem copia do quadro escreve "abcd-2345"; recusar isso seria
    /// recusar o uso normal e devolver a culpa a quem digitou certo.
    /// </summary>
    [Theory]
    [InlineData("ABCD2345")]
    [InlineData("abcd2345")]
    [InlineData("abcd-2345")]
    [InlineData(" ABCD 2345 ")]
    public void Aceita_minusculas_espacos_e_hifens(string bruto)
    {
        Assert.True(CodigoDeTurma.TentarCriar(bruto, out var c, out _));
        Assert.Equal("ABCD2345", c!.Valor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC")]              // curto
    [InlineData("ABCD23456")]        // longo
    public void Recusa_o_que_nao_e_codigo(string? bruto)
    {
        Assert.False(CodigoDeTurma.TentarCriar(bruto, out var c, out var erro));
        Assert.Null(c);
        Assert.False(string.IsNullOrWhiteSpace(erro));
    }

    /// <summary>A mensagem tem de DIZER O QUE FAZER: "inválido" faria tentar o mesmo de novo.</summary>
    [Fact]
    public void Caractere_ambiguo_explica_que_ele_nao_existe_aqui()
    {
        Assert.False(CodigoDeTurma.TentarCriar("ABCD234O", out _, out var erro));
        Assert.Contains("0", erro);
    }
}

public class TurmaTests
{
    private static readonly ApelidoDoUsuario Prof = ApelidoDoUsuario.De("professor");

    [Fact]
    public void Codigo_dono_e_nome_montam_a_turma()
    {
        var t = Turma.TentarCriar(CodigoDeTurma.Sortear(), Prof, "  Contabilidade 2026  ");

        Assert.NotNull(t);
        Assert.Equal("Contabilidade 2026", t.Nome);   // apara as pontas
        Assert.Equal(Prof, t.Dono);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_nome_nao_ha_turma(string? nome)
    {
        // Uma turma sem nome vira "(sem nome)" na lista do aluno, e ele não tem como saber em qual
        // entrou — o nome é a única coisa que o distingue do código.
        Assert.Null(Turma.TentarCriar(CodigoDeTurma.Sortear(), Prof, nome));
    }

    [Fact]
    public void Nome_longo_demais_e_recusado()
    {
        Assert.Null(Turma.TentarCriar(CodigoDeTurma.Sortear(), Prof, new string('x', Turma.TamanhoMaximoDoNome + 1)));
    }

    [Fact]
    public void Sem_dono_nao_ha_turma() =>
        Assert.Null(Turma.TentarCriar(CodigoDeTurma.Sortear(), null, "Turma"));
}
