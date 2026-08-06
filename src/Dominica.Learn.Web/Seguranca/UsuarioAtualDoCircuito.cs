using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// Adaptador de <see cref="IUsuarioAtual"/> sobre a autenticação do ASP.NET.
///
/// É a ÚNICA classe do sistema que traduz "existe um cookie válido" em "este vault é do fulano". Todo o
/// resto — casos de uso, repositório, índice — recebe um apelido e trabalha, sem saber o que é sessão.
///
/// A ORDEM DA CONSULTA IMPORTA: primeiro a seleção explícita do escopo, depois a autenticação. A seleção
/// só tem valor quando alguém a colocou de propósito, e hoje quem faz isso é o vigia do vault, que
/// reconcilia o disco de todo mundo em segundo plano — sem circuito, sem cookie e sem ninguém logado.
/// </summary>
public sealed class UsuarioAtualDoCircuito(
    EscopoDoUsuario escopo,
    AuthenticationStateProvider autenticacao,
    UserManager<ApplicationUser> usuarios) : IUsuarioAtual
{
    private ApelidoDoUsuario? _resolvido;

    public async Task<ApelidoDoUsuario> ApelidoAsync(CancellationToken ct = default)
    {
        if (escopo.Definido is { } declarado) return declarado;
        if (_resolvido is not null) return _resolvido;

        var estado = await autenticacao.GetAuthenticationStateAsync();
        if (estado.User.Identity?.IsAuthenticated != true) throw new SemUsuarioAutenticadoException();

        var usuario = await usuarios.GetUserAsync(estado.User) ?? throw new SemUsuarioAutenticadoException();

        // Conta criada antes do apelido existir, ou registro que passou por um caminho que não o exigiu.
        // Falha alto: o caminho "assume um padrão" levaria duas pessoas para o mesmo vault em silêncio.
        if (!ApelidoDoUsuario.TentarCriar(usuario.Apelido, out var apelido, out var erro) || apelido is null)
            throw new InvalidOperationException(
                $"O usuário \"{usuario.Email}\" está sem apelido válido ({erro}). " +
                "O apelido é o nome da pasta do vault e não pode ser deduzido — corrija o cadastro.");

        return _resolvido = apelido;
    }
}
