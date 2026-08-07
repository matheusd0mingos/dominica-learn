using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// As preferências, guardadas na tabela de identidade.
///
/// LÊ O USUÁRIO A CADA CHAMADA em vez de guardar em cache no escopo: preferência é lida uma vez por
/// carregamento de tela e uma consulta por chave primária custa menos que a chance de a tela mostrar um
/// valor velho logo depois de a pessoa tê-lo mudado.
///
/// SEM SESSÃO, DEVOLVE O PADRÃO em vez de lançar. O vigia do vault reconcilia em segundo plano e não tem
/// preferência de ninguém; fazer isso explodir transformaria um ajuste de conforto em causa de falha de
/// um serviço que não precisa dele.
/// </summary>
public sealed class PreferenciasNoIdentity(
    AuthenticationStateProvider autenticacao,
    UserManager<ApplicationUser> usuarios) : IPreferenciasDoUsuario
{
    public async Task<int> CartoesNovosPorDiaAsync(CancellationToken ct = default) =>
        (await EuAsync())?.CartoesNovosPorDia ?? TetoDeCartoesNovos.Padrao;

    public async Task DefinirCartoesNovosPorDiaAsync(int quantos, CancellationToken ct = default)
    {
        var eu = await EuAsync();
        if (eu is null) return;

        // Teto negativo não significa nada, e um teto absurdo é o mesmo que não ter teto — com o
        // agravante de parecer que existe um. Zero continua sendo o "sem teto" explícito.
        eu.CartoesNovosPorDia = Math.Clamp(quantos, TetoDeCartoesNovos.SemTeto, 999);
        await usuarios.UpdateAsync(eu);
    }

    public async Task<NomeDoVault?> VaultAtualAsync(CancellationToken ct = default) =>
        // NomeDoVault.Conhecido, e não De: o campo pode apontar para um vault renomeado ou apagado por
        // fora, e isso não é motivo para a tela explodir. Nulo aqui vira o padrão em quem pergunta.
        NomeDoVault.Conhecido((await EuAsync())?.VaultAtual);

    public async Task DefinirVaultAtualAsync(NomeDoVault vault, CancellationToken ct = default)
    {
        var eu = await EuAsync();
        if (eu is null) return;

        eu.VaultAtual = vault.Valor;
        await usuarios.UpdateAsync(eu);
    }

    private async Task<ApplicationUser?> EuAsync()
    {
        try
        {
            var estado = await autenticacao.GetAuthenticationStateAsync();
            if (estado.User.Identity?.IsAuthenticated != true) return null;
            return await usuarios.GetUserAsync(estado.User);
        }
        catch (InvalidOperationException)
        {
            // Fora de um componente Razor o provedor de estado do Blazor lança. Ver UsuarioAtualDoCircuito:
            // é o mesmo limite, e aqui a resposta certa é "não sei", não "quebre".
            return null;
        }
    }
}
