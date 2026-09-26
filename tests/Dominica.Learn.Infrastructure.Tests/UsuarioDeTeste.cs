namespace Dominica.Learn.Infrastructure.Tests;

/// <summary>
/// Um usuário fixo, para teste. O que ele prova de passagem: o adaptador de disco não sabe o que é
/// sessão nem cookie — ele recebe um apelido e trabalha.
/// </summary>
internal sealed class UsuarioDeTeste(string apelido, string? vault = null)
    : Dominica.Learn.Application.Portas.IUsuarioAtual
{
    public Task<Dominica.Learn.Domain.Vault.ApelidoDoUsuario> ApelidoAsync(CancellationToken ct = default) =>
        Task.FromResult(Dominica.Learn.Domain.Vault.ApelidoDoUsuario.De(apelido));

    // Sem vault dito, o padrão — que é onde a migração pôs o que já existia, e é onde estão os testes
    // que foram escritos antes de haver mais de um vault.
    public Task<Dominica.Learn.Domain.Vault.NomeDoVault> VaultAsync(CancellationToken ct = default) =>
        Task.FromResult(vault is null
            ? Dominica.Learn.Domain.Vault.NomeDoVault.Padrao
            : Dominica.Learn.Domain.Vault.NomeDoVault.De(vault));
}
