using System.Security.Claims;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

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
/// <remarks>
/// ESCOPO PRÓPRIO POR CONSULTA, e não o UserManager do circuito. O ApplicationDbContext é registrado
/// com AddDbContext — um por circuito, vivo por horas — e duas leituras sobrepostas nele fazem o EF
/// lançar "a second operation was started on this context instance". A exceção sobe pela renderização
/// e MATA O CIRCUITO: a tela fica desenhada e para de responder.
///
/// Isso não era possível enquanto só o apelido era perguntado, uma vez por escopo. Passou a ser no dia
/// em que o VAULT entrou na fronteira: ele é consultado em toda operação de arquivo e de índice, e a
/// barra do topo o pergunta ao mesmo tempo que a página. Aconteceu na primeira tela depois do deploy.
///
/// É a mesma correção que o índice e o registro já tinham recebido (ver IndiceEmPostgres.AbrirAsync) e
/// o mesmo caminho que o IdentityRevalidatingAuthenticationStateProvider já usava aqui do lado.
/// </remarks>
public sealed class ApelidoDeQuemEntrou(IServiceScopeFactory escopos)
{
    public async Task<ApelidoDoUsuario> DeAsync(ClaimsPrincipal quem)
    {
        if (quem.Identity?.IsAuthenticated != true) throw new SemUsuarioAutenticadoException();

        await using var escopo = escopos.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var usuario = await usuarios.GetUserAsync(quem) ?? throw new SemUsuarioAutenticadoException();

        // Conta criada antes do apelido existir, ou registro que passou por um caminho que não o exigiu.
        // Falha alto: o caminho "assume um padrão" levaria duas pessoas para o mesmo vault em silêncio.
        if (!ApelidoDoUsuario.TentarCriar(usuario.Apelido, out var apelido, out var erro) || apelido is null)
            throw new InvalidOperationException(
                $"O usuário \"{usuario.Email}\" está sem apelido válido ({erro}). " +
                "O apelido é o nome da pasta do vault e não pode ser deduzido — corrija o cadastro.");

        return apelido;
    }

    /// <summary>
    /// E em qual vault DELE. Mesma razão de a classe existir: a pergunta chega pelo circuito e pelo
    /// endpoint do download, e duas traduções para manter iguais é uma a mais do que se consegue.
    ///
    /// AQUI NÃO SE FALHA ALTO, ao contrário do apelido. Vault vazio é o estado normal de quem nunca
    /// escolheu; vault apagado por fora é o estado normal de quem mexeu na pasta. Nos dois casos a
    /// resposta certa é o padrão, e não uma exceção — quem cai no padrão vê o vault de estudo, que é
    /// onde tudo estava antes de haver mais de um.
    /// </summary>
    public async Task<NomeDoVault> VaultDeAsync(ClaimsPrincipal quem)
    {
        if (quem.Identity?.IsAuthenticated != true) throw new SemUsuarioAutenticadoException();

        await using var escopo = escopos.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var usuario = await usuarios.GetUserAsync(quem);
        return NomeDoVault.Conhecido(usuario?.VaultAtual) ?? NomeDoVault.Padrao;
    }
}
