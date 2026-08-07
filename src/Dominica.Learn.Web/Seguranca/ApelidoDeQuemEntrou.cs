using System.Security.Claims;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// Dado alguém autenticado, de quem é o vault.
///
/// EXISTE SEPARADO porque a mesma pergunta chega por dois caminhos que não se parecem em nada: um
/// componente Blazor, que só conhece o estado do circuito, e um endpoint HTTP comum (o download do
/// pacote), que só conhece o <see cref="HttpContext"/>. Se cada um traduzisse "cookie válido" em "pasta
/// do fulano" por conta própria, seriam duas traduções para manter iguais — e o dia em que
/// divergissem, uma delas entregaria o vault errado sem erro nenhum.
///
/// A regra dura mora aqui: sem apelido válido no cadastro, ninguém tem vault. Deduzir um a partir do
/// e-mail levaria duas pessoas para a mesma pasta em silêncio.
/// </summary>
public sealed class ApelidoDeQuemEntrou(UserManager<ApplicationUser> usuarios)
{
    public async Task<ApelidoDoUsuario> DeAsync(ClaimsPrincipal quem)
    {
        if (quem.Identity?.IsAuthenticated != true) throw new SemUsuarioAutenticadoException();

        var usuario = await usuarios.GetUserAsync(quem) ?? throw new SemUsuarioAutenticadoException();

        // Conta criada antes do apelido existir, ou registro que passou por um caminho que não o exigiu.
        // Falha alto: o caminho "assume um padrão" levaria duas pessoas para o mesmo vault em silêncio.
        if (!ApelidoDoUsuario.TentarCriar(usuario.Apelido, out var apelido, out var erro) || apelido is null)
            throw new InvalidOperationException(
                $"O usuário \"{usuario.Email}\" está sem apelido válido ({erro}). " +
                "O apelido é o nome da pasta do vault e não pode ser deduzido — corrija o cadastro.");

        return apelido;
    }
}
