using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Xunit;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// As regras de quem pode acompanhar os estudos de quem — as que valem antes de qualquer banco.
/// </summary>
public class AcompanhamentoTests
{
    private static ApelidoDoUsuario Apelido(string v) => ApelidoDoUsuario.De(v);

    [Fact]
    public void Dono_vault_e_convidado_montam_o_acompanhamento()
    {
        var a = Acompanhamento.TentarCriar(Apelido("matheus"), NomeDoVault.Padrao, Apelido("rodrigo"));

        Assert.NotNull(a);
        Assert.Equal("matheus", a.Dono.Valor);
        Assert.Equal("rodrigo", a.Convidado.Valor);
        Assert.Equal(NomeDoVault.Padrao, a.Vault);
    }

    /// <summary>
    /// ACOMPANHAR A SI MESMO não é um caso a tratar mais adiante: é uma linha sem significado que faria
    /// a lista de "quem me acompanha" mentir — e que daria a alguém a impressão de estar sendo observado
    /// por si próprio.
    /// </summary>
    [Fact]
    public void Ninguem_acompanha_a_si_mesmo()
    {
        Assert.Null(Acompanhamento.TentarCriar(Apelido("matheus"), NomeDoVault.Padrao, Apelido("matheus")));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Sem_qualquer_uma_das_tres_pontas_nao_ha_acompanhamento(bool semDono, bool semVault, bool semConvidado)
    {
        var a = Acompanhamento.TentarCriar(
            semDono ? null : Apelido("matheus"),
            semVault ? null : NomeDoVault.Padrao,
            semConvidado ? null : Apelido("rodrigo"));

        Assert.Null(a);
    }

    /// <summary>
    /// O VAULT FAZ PARTE DA IDENTIDADE DA PERMISSÃO. Duas concessões para a mesma dupla de pessoas em
    /// vaults diferentes são coisas DIFERENTES — quem abriu o vault de estudo não abriu o de trabalho.
    /// Como o tipo é um record, é a igualdade estrutural que garante isso, e é ela que a chave primária
    /// da tabela espelha.
    /// </summary>
    [Fact]
    public void Mesmo_par_de_pessoas_em_vaults_diferentes_sao_acompanhamentos_diferentes()
    {
        var estudo = Acompanhamento.TentarCriar(Apelido("matheus"), NomeDoVault.De("estudo"), Apelido("rodrigo"));
        var trabalho = Acompanhamento.TentarCriar(Apelido("matheus"), NomeDoVault.De("trabalho"), Apelido("rodrigo"));

        Assert.NotEqual(estudo, trabalho);
    }

    /// <summary>
    /// A DIREÇÃO IMPORTA: eu deixar você ver os meus estudos não deixa você ver os meus. Parece óbvio, e
    /// é exatamente o tipo de coisa que uma consulta escrita às pressas troca — um `x.Dono == convidado`
    /// no lugar de `x.Convidado == convidado` inverte o mundo inteiro sem dar erro.
    /// </summary>
    [Fact]
    public void Acompanhamento_nao_e_reciproco()
    {
        var ida = Acompanhamento.TentarCriar(Apelido("matheus"), NomeDoVault.Padrao, Apelido("rodrigo"));
        var volta = Acompanhamento.TentarCriar(Apelido("rodrigo"), NomeDoVault.Padrao, Apelido("matheus"));

        Assert.NotEqual(ida, volta);
    }
}
