using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

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
/// <remarks>
/// ESCOPO PRÓPRIO POR CONSULTA — mesma razão e mesma correção de ApelidoDeQuemEntrou: o
/// ApplicationDbContext do circuito não aguenta duas leituras sobrepostas, e desde que o vault entrou
/// na fronteira este adaptador passou a ser consultado de vários lugares ao mesmo tempo.
/// </remarks>
public sealed class PreferenciasNoIdentity(
    AuthenticationStateProvider autenticacao,
    IServiceScopeFactory escopos) : IPreferenciasDoUsuario
{
    public Task<int> CartoesNovosPorDiaAsync(CancellationToken ct = default) =>
        ComOUsuarioAsync((_, eu) => Task.FromResult(eu?.CartoesNovosPorDia ?? TetoDeCartoesNovos.Padrao));

    public Task DefinirCartoesNovosPorDiaAsync(int quantos, CancellationToken ct = default) =>
        ComOUsuarioAsync(async (usuarios, eu) =>
        {
            if (eu is null) return 0;
            // Teto negativo não significa nada, e um teto absurdo é o mesmo que não ter teto — com o
            // agravante de parecer que existe um. Zero continua sendo o "sem teto" explícito.
            eu.CartoesNovosPorDia = Math.Clamp(quantos, TetoDeCartoesNovos.SemTeto, 999);
            await usuarios.UpdateAsync(eu);
            return 0;
        });

    public Task<NomeDoVault?> VaultAtualAsync(CancellationToken ct = default) =>
        // NomeDoVault.Conhecido, e não De: o campo pode apontar para um vault renomeado ou apagado por
        // fora, e isso não é motivo para a tela explodir. Nulo aqui vira o padrão em quem pergunta.
        ComOUsuarioAsync((_, eu) => Task.FromResult(NomeDoVault.Conhecido(eu?.VaultAtual)));

    public Task DefinirVaultAtualAsync(NomeDoVault vault, CancellationToken ct = default) =>
        ComOUsuarioAsync(async (usuarios, eu) =>
        {
            if (eu is null) return 0;
            eu.VaultAtual = vault.Valor;
            await usuarios.UpdateAsync(eu);
            return 0;
        });

    /// <summary>
    /// Abre um escopo, resolve o usuário nele e entrega os dois a quem chamou. Um contexto por
    /// operação — ver o comentário da classe.
    /// </summary>
    private async Task<T> ComOUsuarioAsync<T>(Func<UserManager<ApplicationUser>, ApplicationUser?, Task<T>> fazer)
    {
        try
        {
            var estado = await autenticacao.GetAuthenticationStateAsync();
            await using var escopo = escopos.CreateAsyncScope();
            var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var eu = estado.User.Identity?.IsAuthenticated == true
                ? await usuarios.GetUserAsync(estado.User)
                : null;

            return await fazer(usuarios, eu);
        }
        catch (InvalidOperationException)
        {
            // Fora de um componente Razor o provedor de estado do Blazor lança. Ver UsuarioAtualDoCircuito:
            // é o mesmo limite, e aqui a resposta certa é "não sei", não "quebre".
            return await fazer(null!, null);
        }
    }
}
