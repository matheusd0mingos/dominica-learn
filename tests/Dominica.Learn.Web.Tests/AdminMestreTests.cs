using System.Security.Claims;
using Dominica.Learn.Web.Seguranca;

namespace Dominica.Learn.Web.Tests;

/// <summary>
/// O ADMIN MESTRE é uma regra de cinco linhas que decide quem administra o sistema. Regra pequena é
/// justamente a que ninguém relê antes de mexer — por isso ela tem teste.
/// </summary>
public class AdminMestreTests
{
    private static ClaimsPrincipal Logado(string email, bool comClaimDeEmail = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, email) };
        if (comClaimDeEmail) claims.Add(new Claim(ClaimTypes.Email, email));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "teste"));
    }

    private static OpcoesDeAdministracao Com(string admin) => new() { EmailDoAdminMestre = admin };

    [Fact]
    public void Reconhece_o_admin_mestre_pelo_email() =>
        Assert.True(Com("eu@dominica.app.br").EhAdminMestre(Logado("eu@dominica.app.br")));

    [Fact]
    public void Caixa_do_email_nao_importa() =>
        Assert.True(Com("eu@dominica.app.br").EhAdminMestre(Logado("EU@Dominica.App.BR")));

    [Fact]
    public void Reconhece_mesmo_sem_a_claim_de_email()
    {
        // O template do Identity usa o e-mail como nome de usuário, e dependendo do caminho de login a
        // claim de e-mail não vem. Sem esta rede, o dono do sistema não se reconheceria como admin.
        Assert.True(Com("eu@dominica.app.br").EhAdminMestre(Logado("eu@dominica.app.br", comClaimDeEmail: false)));
    }

    [Fact]
    public void Outra_pessoa_nao_e_admin() =>
        Assert.False(Com("eu@dominica.app.br").EhAdminMestre(Logado("joao@exemplo.com")));

    [Fact]
    public void Sem_admin_configurado_ninguem_e_admin()
    {
        // FALHA FECHADA: configuração vazia não pode virar "todo mundo é admin", que é o desastre clássico
        // de comparar string vazia com string vazia.
        Assert.False(Com("").EhAdminMestre(Logado("")));
        Assert.False(Com("").EhAdminMestre(Logado("qualquer@exemplo.com")));
        Assert.False(Com("   ").EhAdminMestre(Logado("   ")));
    }

    [Fact]
    public void Anonimo_nunca_e_admin()
    {
        Assert.False(Com("eu@dominica.app.br").EhAdminMestre(null));
        Assert.False(Com("eu@dominica.app.br").EhAdminMestre(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void Cadastro_e_fechado_por_padrao()
    {
        // Aberto, qualquer um que descubra a URL cria conta e ocupa disco do VPS. O padrão certo para um
        // sistema de uso familiar é a porta fechada; abrir tem de ser um ato escrito no appsettings.
        Assert.False(new OpcoesDeAdministracao().CadastroAberto);
    }
}
