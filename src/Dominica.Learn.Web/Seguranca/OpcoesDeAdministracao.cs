using System.Security.Claims;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// Quem manda no Learn, e quem pode entrar.
///
/// O ADMIN MESTRE É O MESMO DA DOMINICA — a mesma pessoa, o mesmo e-mail —, mas a CONTA é daqui. Essa
/// distinção é o desenho inteiro: o Learn não consulta o emissor de identidade da plataforma para
/// funcionar, então continua de pé se a Dominica sair do ar, e a especificação de independência segue
/// valendo. O que se compartilha é uma linha de configuração, não uma dependência de execução.
///
/// Vem de appsettings/variável de ambiente, nunca do código. Trocar o dono do sistema não pode exigir
/// recompilar — e um e-mail de administrador fixo no fonte é o tipo de coisa que vaza em repositório.
/// </summary>
public sealed class OpcoesDeAdministracao
{
    public const string Secao = "Administracao";

    /// <summary>E-mail do admin mestre. Vazio = ninguém é admin, e a tela de administração não abre.</summary>
    public string EmailDoAdminMestre { get; set; } = string.Empty;

    /// <summary>
    /// Cadastro aberto a qualquer um?
    ///
    /// FECHADO POR PADRÃO, e isto é uma decisão de segurança, não de produto. Aberto, qualquer pessoa que
    /// descubra a URL cria conta e passa a ocupar disco no VPS. O isolamento por usuário impede que ela
    /// veja a nota de alguém — não impede que ela exista. Para um sistema de uso familiar, o padrão certo
    /// é a porta fechada; abrir é um ato consciente, escrito no appsettings.
    ///
    /// Com o cadastro fechado, quem cria conta é o admin mestre, pela mesma tela de sempre.
    /// </summary>
    public bool CadastroAberto { get; set; }

    /// <summary>
    /// Esta pessoa é o admin mestre?
    ///
    /// Compara pelo e-mail porque é o que a plataforma e o Learn têm em comum — o apelido é local daqui,
    /// e o id do usuário é diferente nos dois lados por construção.
    /// </summary>
    public bool EhAdminMestre(ClaimsPrincipal? quem)
    {
        if (quem?.Identity?.IsAuthenticated != true) return false;
        if (string.IsNullOrWhiteSpace(EmailDoAdminMestre)) return false;

        // O template do Identity usa o e-mail como nome de usuário, mas a claim de e-mail nem sempre vem
        // preenchida dependendo do caminho de login. Conferir os dois evita um admin que não se reconhece.
        var email = quem.FindFirstValue(ClaimTypes.Email) ?? quem.Identity.Name;
        return string.Equals(email, EmailDoAdminMestre.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A política de autorização que protege a tela de administração.</summary>
    public const string Politica = "AdminMestre";
}
