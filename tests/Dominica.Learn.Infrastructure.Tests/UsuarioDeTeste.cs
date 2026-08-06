namespace Dominica.Learn.Infrastructure.Tests;

/// <summary>
/// Um usuário fixo, para teste. O que ele prova de passagem: o adaptador de disco não sabe o que é
/// sessão nem cookie — ele recebe um apelido e trabalha.
/// </summary>
internal sealed class UsuarioDeTeste(string apelido) : Dominica.Learn.Application.Portas.IUsuarioAtual
{
    public Task<Dominica.Learn.Domain.Vault.ApelidoDoUsuario> ApelidoAsync(CancellationToken ct = default) =>
        Task.FromResult(Dominica.Learn.Domain.Vault.ApelidoDoUsuario.De(apelido));
}
