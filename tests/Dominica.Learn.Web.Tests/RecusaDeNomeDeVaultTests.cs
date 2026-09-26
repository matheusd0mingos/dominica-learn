using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Web.Tests;

/// <summary>
/// A RECUSA DE UM NOME DE VAULT PRECISA CHEGAR NA TELA.
///
/// Chegou como "criar não está criando": quem digita um nome fora da regra (espaço, acento, sublinhado)
/// só via a tela piscar e voltar vazia — o motivo ia para o log do servidor, que é o único lugar onde
/// quem usa o produto nunca olha. O conserto foi devolver o nome tentado na URL e deixar a TELA
/// reexecutar esta mesma validação para imprimir o porquê.
///
/// O que estes testes guardam é o contrato de que a validação tem uma mensagem para cada recusa — sem
/// ela, a tela volta a não ter o que dizer.
/// </summary>
public class RecusaDeNomeDeVaultTests
{
    [Theory]
    [InlineData("Notas Pessoais")]   // espaço — o caso mais provável de quem nomeia como nomearia uma pasta
    [InlineData("revisão")]          // acento
    [InlineData("meu_vault")]        // sublinhado
    [InlineData("2026")]             // não começa com letra
    [InlineData("a")]                // curto demais
    [InlineData("trabalho-")]        // termina com hífen
    public void Nome_fora_da_regra_e_recusado_COM_motivo(string tentado)
    {
        var valeu = NomeDoVault.TentarCriar(tentado, out var vault, out var porQue);

        Assert.False(valeu);
        Assert.Null(vault);
        Assert.False(string.IsNullOrWhiteSpace(porQue), $"\"{tentado}\" foi recusado sem explicação — a tela não teria o que mostrar.");
    }

    [Theory]
    [InlineData("trabalho", "trabalho")]
    [InlineData("Trabalho", "trabalho")]      // maiúscula não é erro: o domínio normaliza
    [InlineData("  concurso-tcu  ", "concurso-tcu")]
    public void Nome_valido_passa_normalizado_e_sem_motivo(string tentado, string esperado)
    {
        var valeu = NomeDoVault.TentarCriar(tentado, out var vault, out var porQue);

        Assert.True(valeu, porQue);
        Assert.Equal(esperado, vault!.Valor);
        Assert.Null(porQue);
    }
}
