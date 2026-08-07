using System.Net;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Web.Components.Account;

/// <summary>
/// Traduz os três e-mails que o Identity sabe pedir para a porta <see cref="IEnviadorDeEmail"/>.
///
/// POR QUE ESTA CLASSE EXISTE em vez de o Identity falar direto com o SMTP: o contrato dele
/// (<c>IEmailSender&lt;ApplicationUser&gt;</c>) é do framework, fala inglês e conhece
/// <c>ApplicationUser</c>. Nada disso deveria atravessar a fronteira da infraestrutura. Aqui é a
/// costura — e é o único lugar do sistema onde o TEXTO dos e-mails mora.
///
/// O TEXTO É PARTE DO PRODUTO, e não uma string qualquer. Quem recebe um e-mail de redefinir senha
/// está, quase sempre, no pior momento possível com o app: perdeu o acesso. A mensagem tem de ser
/// curta, dizer quem mandou, e dizer o que fazer se não foi a pessoa que pediu — porque um e-mail
/// desses que chega sem ninguém ter pedido é o primeiro sinal de que alguém está tentando entrar.
/// </summary>
/// <remarks>
/// FALHA DE ENVIO NÃO PODE DERRUBAR A TELA, e esta é a diferença entre a porta e este adaptador.
///
/// O <see cref="EmailPorSmtp"/> relança de propósito: engolir lá transformaria "não chegou" em "não
/// aconteceu nada", que é indepurável. Mas o contrato do Identity NÃO TEM canal de falha — quem chama
/// é a tela de cadastro, que já criou a conta quando manda o e-mail. Deixar a exceção subir dali
/// quebrava a tela DEPOIS de a conta existir, e a pessoa ficava sem saber se ela existe ou não.
/// Aconteceu em produção, com o SMTP configurado e o servidor recusando.
///
/// Então a decisão é: o e-mail é BEST-EFFORT e o cadastro é o que importa. A falha vira erro no log,
/// com destino e assunto, e o fluxo segue. Quem não recebeu tem "reenviar confirmação" e
/// "esqueci minha frase" — os dois caminhos existem e funcionam.
/// </remarks>
public sealed class EmailDoIdentity(IEnviadorDeEmail enviador, ILogger<EmailDoIdentity> log) : IEmailSender<ApplicationUser>
{
    /// <summary>Manda, e se não der, registra e segue. Ver o comentário da classe.</summary>
    private async Task TentarAsync(MensagemDeEmail mensagem)
    {
        try
        {
            await enviador.EnviarAsync(mensagem);
        }
        catch (Exception e)
        {
            log.LogError(e, "Não consegui enviar \"{Assunto}\" para {Destino}. O fluxo seguiu sem o e-mail.",
                mensagem.Assunto, mensagem.Para);
        }
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        TentarAsync(new MensagemDeEmail(
            email,
            "Confirme seu e-mail — Dominica Learn",
            Corpo(
                "Confirme seu e-mail",
                "Falta um passo para a sua conta no Dominica Learn ficar pronta.",
                confirmationLink,
                "Confirmar e-mail",
                "Se não foi você que criou esta conta, ignore esta mensagem — nada acontece sem o clique.")));

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        TentarAsync(new MensagemDeEmail(
            email,
            "Redefinir sua frase secreta — Dominica Learn",
            Corpo(
                "Redefinir a frase secreta",
                "Alguém pediu para redefinir a frase secreta desta conta no Dominica Learn.",
                resetLink,
                "Criar uma nova frase secreta",
                "Se não foi você, ignore esta mensagem: a frase atual continua valendo e ninguém entrou. "
                + "Mas se isto se repetir, vale trocá-la.")));

    // O `resetCode` vem do Identity, e não deste arquivo: é o único valor aqui que precisa ser escapado.
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        TentarAsync(new MensagemDeEmail(
            email,
            "Seu código de redefinição — Dominica Learn",
            $"""
             <p>Alguém pediu para redefinir a frase secreta desta conta no Dominica Learn.</p>
             <p>O código é: <b style="font-size:1.3em;letter-spacing:.1em">{WebUtility.HtmlEncode(resetCode)}</b></p>
             <p>Se não foi você, ignore esta mensagem: a frase atual continua valendo.</p>
             <p style="color:#666;font-size:.9em">Dominica Learn</p>
             """));

    /// <summary>
    /// O molde dos e-mails com link.
    ///
    /// O ENDEREÇO APARECE ESCRITO, ABAIXO DO BOTÃO, e isso não é redundância. Cliente de e-mail que
    /// bloqueia HTML, cliente de texto puro e encaminhamento para outro aplicativo — em todos esses
    /// casos o botão some e o link escrito é o que salva. É a diferença entre "não funcionou" e
    /// "deu para copiar e colar".
    ///
    /// NADA AQUI É CODIFICADO, e cada omissão tem uma razão diferente.
    ///
    ///   O TEXTO são literais deste arquivo, escritos por quem mantém o produto. Passá-los por
    ///   HtmlEncode não protege de nada — não há entrada de usuário nenhuma neste caminho — e troca
    ///   todo acento por entidade numérica (&amp;#233; no lugar de é), o que só piora a leitura de
    ///   quem for conferir o corpo no log ou no código.
    ///
    ///   O LINK já vem passado por HtmlEncoder na tela que pediu o envio. Codificar de novo viraria
    ///   &amp;amp; no href, e o app receberia um parâmetro chamado "amp;returnUrl" que ele ignora — um
    ///   link que abre e não faz o que deveria. Ver EmailDoIdentityTests.
    ///
    /// O único valor que NÃO é nosso neste arquivo é o código de redefinição, e ele é escapado.
    /// </summary>
    private static string Corpo(string titulo, string explicacao, string link, string rotuloDoBotao, string seNaoFoiVoce) =>
        $"""
         <p>{explicacao}</p>
         <p>
           <a href="{link}"
              style="display:inline-block;padding:.7em 1.4em;background:#0f2340;color:#fff;
                     text-decoration:none;border-radius:6px">{rotuloDoBotao}</a>
         </p>
         <p style="color:#666;font-size:.9em">
           Se o botão não funcionar, copie este endereço para o navegador:<br />
           <span style="word-break:break-all">{link}</span>
         </p>
         <p>{seNaoFoiVoce}</p>
         <p style="color:#666;font-size:.9em">Dominica Learn — {titulo}</p>
         """;
}
