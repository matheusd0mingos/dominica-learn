using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

// O apelido vira NOME DE PASTA no disco e segmento de URL. Toda recusa aqui é uma pasta que não vai
// existir com nome estranho, ou uma rota que não vai colidir.
public class ApelidoDoUsuarioTests
{
    [Theory]
    [InlineData("matheus")]
    [InlineData("ana-clara")]
    [InlineData("jose2026")]
    public void Apelido_bem_formado_e_aceito(string bruto) =>
        Assert.True(ApelidoDoUsuario.TentarCriar(bruto, out _, out _));

    [Fact]
    public void Maiusculas_e_espaco_em_volta_sao_normalizados()
    {
        Assert.True(ApelidoDoUsuario.TentarCriar("  Matheus  ", out var a, out _));
        Assert.Equal("matheus", a!.Valor);
    }

    [Fact]
    public void Acento_e_recusado_e_nao_normalizado()
    {
        // "josé" e "jose" virariam a MESMA pasta em alguns sistemas de arquivos e duas em outros. Aceitar
        // e normalizar esconderia essa ambiguidade até o dia em que ela custasse caro.
        Assert.False(ApelidoDoUsuario.TentarCriar("josé", out _, out var erro));
        Assert.Contains("sem acento", erro);
    }

    [Theory]
    [InlineData("ma the us")]
    [InlineData("matheus/outro")]
    [InlineData("../etc")]
    [InlineData("mat.heus")]
    [InlineData("mat_heus")]
    public void Nada_que_atrapalhe_um_caminho_passa(string bruto) =>
        Assert.False(ApelidoDoUsuario.TentarCriar(bruto, out _, out _));

    [Fact]
    public void Precisa_comecar_com_letra()
    {
        // "2024" viraria uma pasta indistinguível de uma pasta de ano; "-x" é lido como opção por vários
        // comandos de terminal.
        Assert.False(ApelidoDoUsuario.TentarCriar("2024", out _, out _));
        Assert.False(ApelidoDoUsuario.TentarCriar("-matheus", out _, out _));
    }

    [Theory]
    [InlineData("notas")]
    [InlineData("grafo")]
    [InlineData("anexos")]
    [InlineData("admin")]
    [InlineData("templates")]
    [InlineData("con")]
    public void Nomes_reservados_sao_recusados(string bruto)
    {
        // Rota, pasta de sistema, ou algo que já significa outra coisa dentro do vault. Um usuário
        // "anexos" teria a pasta dele confundida com a pasta de anexos.
        Assert.False(ApelidoDoUsuario.TentarCriar(bruto, out _, out var erro));
        Assert.Contains("reservado", erro);
    }

    [Fact]
    public void Reservado_e_recusado_mesmo_escrito_em_maiuscula()
    {
        Assert.False(ApelidoDoUsuario.TentarCriar("ADMIN", out _, out _));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("a")]
    public void Curto_demais_e_recusado(string bruto) =>
        Assert.False(ApelidoDoUsuario.TentarCriar(bruto, out _, out _));

    [Fact]
    public void Longo_demais_e_recusado() =>
        Assert.False(ApelidoDoUsuario.TentarCriar(new string('a', ApelidoDoUsuario.TamanhoMaximo + 1), out _, out _));

    [Theory]
    [InlineData("matheus-")]
    [InlineData("ma--theus")]
    public void Hifen_no_fim_ou_dobrado_e_recusado(string bruto) =>
        Assert.False(ApelidoDoUsuario.TentarCriar(bruto, out _, out _));

    [Fact]
    public void Apelido_vira_um_segmento_de_caminho_seguro()
    {
        // A garantia que importa para o disco: o que passa daqui é sempre UM segmento, sem escape.
        Assert.True(ApelidoDoUsuario.TentarCriar("ana-clara", out var a, out _));
        Assert.DoesNotContain('/', a!.Valor);
        Assert.DoesNotContain('\\', a.Valor);
        Assert.NotEqual("..", a.Valor);
    }
}
