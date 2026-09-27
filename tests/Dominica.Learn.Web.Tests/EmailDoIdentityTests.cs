using Dominica.Learn.Application.Portas;
using Dominica.Learn.Web.Components.Account;
using Dominica.Learn.Web.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Web.Tests;

/// <summary>
/// O TEXTO DOS E-MAILS É PRODUTO, e o link dentro deles é a única coisa que precisa funcionar.
///
/// Quem recebe estas mensagens está no pior momento possível com o app: perdeu o acesso. Um link
/// quebrado aqui não gera reclamação — gera silêncio, porque a pessoa não tem por onde reclamar.
/// </summary>
public class EmailDoIdentityTests
{
    private sealed class EnviadorDeMentira : IEnviadorDeEmail
    {
        public MensagemDeEmail? Ultima { get; private set; }
        public bool Ligado => true;

        public Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default)
        {
            Ultima = mensagem;
            return Task.CompletedTask;
        }
    }

    private static (EmailDoIdentity email, EnviadorDeMentira caixa) Montar()
    {
        var caixa = new EnviadorDeMentira();
        return (new EmailDoIdentity(caixa, NullLogger<EmailDoIdentity>.Instance), caixa);
    }

    /// <summary>Um enviador que sempre falha — é o servidor SMTP recusando.</summary>
    private sealed class EnviadorQueFalha : IEnviadorDeEmail
    {
        public bool Ligado => true;
        public Task EnviarAsync(MensagemDeEmail m, CancellationToken ct = default) =>
            throw new InvalidOperationException("servidor recusou");
    }

    [Fact]
    public async Task Falha_no_envio_nao_derruba_quem_chamou()
    {
        // O DEFEITO QUE ISTO PEGA JÁ ACONTECEU EM PRODUÇÃO: com o SMTP configurado e o servidor
        // recusando, a exceção subia da tela de CADASTRO — depois de a conta já ter sido criada. A
        // pessoa via "esta tela quebrou" e ficava sem saber se tinha conta.
        //
        // O contrato do Identity não tem canal de falha, então o adaptador não pode ter um: e-mail é
        // best-effort, cadastro é o que importa. A falha vira erro no log.
        var email = new EmailDoIdentity(new EnviadorQueFalha(), NullLogger<EmailDoIdentity>.Instance);

        await email.SendConfirmationLinkAsync(new ApplicationUser(), "eu@exemplo.com", "https://x/y");
        await email.SendPasswordResetLinkAsync(new ApplicationUser(), "eu@exemplo.com", "https://x/y");
        await email.SendPasswordResetCodeAsync(new ApplicationUser(), "eu@exemplo.com", "123");
    }

    // O link que o Identity entrega já vem passado por HtmlEncoder na tela que pediu o envio: os `&`
    // da query chegam como `&amp;`. É essa forma que tem de ir para o HTML.
    private const string LinkComoOIdentityEntrega =
        "https://learn.exemplo.com.br/Account/ResetPassword?code=abc&amp;returnUrl=%2Fnotas";

    [Fact]
    public async Task O_link_de_redefinir_vai_inteiro_e_sem_codificar_de_novo()
    {
        // A CODIFICAÇÃO DUPLA É O DEFEITO QUE ESTE TESTE EXISTE PARA PEGAR. Passar o link por
        // HtmlEncode aqui viraria `&amp;amp;` no href — e o navegador levaria a pessoa para uma URL
        // com um parâmetro chamado "amp;returnUrl", que o app ignora. O link "funciona" e não faz o
        // que deveria, que é a pior forma de quebrar.
        var (email, caixa) = Montar();
        await email.SendPasswordResetLinkAsync(new ApplicationUser(), "eu@exemplo.com", LinkComoOIdentityEntrega);

        Assert.NotNull(caixa.Ultima);
        Assert.Contains($"href=\"{LinkComoOIdentityEntrega}\"", caixa.Ultima!.CorpoHtml);
        Assert.DoesNotContain("&amp;amp;", caixa.Ultima.CorpoHtml);
    }

    [Fact]
    public async Task O_endereco_tambem_aparece_escrito_para_copiar()
    {
        // Cliente de e-mail que bloqueia HTML come o botão. O endereço escrito é o que sobra.
        var (email, caixa) = Montar();
        await email.SendPasswordResetLinkAsync(new ApplicationUser(), "eu@exemplo.com", LinkComoOIdentityEntrega);

        var corpo = caixa.Ultima!.CorpoHtml;
        var ocorrencias = corpo.Split(LinkComoOIdentityEntrega).Length - 1;
        Assert.Equal(2, ocorrencias);   // uma no href, uma escrita
    }

    [Fact]
    public async Task A_mensagem_vai_para_quem_pediu_e_em_portugues()
    {
        var (email, caixa) = Montar();
        await email.SendPasswordResetLinkAsync(new ApplicationUser(), "eu@exemplo.com", LinkComoOIdentityEntrega);

        Assert.Equal("eu@exemplo.com", caixa.Ultima!.Para);
        Assert.Contains("frase secreta", caixa.Ultima.Assunto);
        Assert.Contains("Dominica Learn", caixa.Ultima.Assunto);
    }

    [Fact]
    public async Task Quem_nao_pediu_e_avisado_de_que_nada_aconteceu()
    {
        // Um e-mail de redefinição que chega sem ninguém ter pedido é o primeiro sinal de que alguém
        // está tentando entrar. A mensagem tem de dizer o que fazer — e, antes disso, tranquilizar:
        // nada mudou só porque o e-mail chegou.
        var (email, caixa) = Montar();
        await email.SendPasswordResetLinkAsync(new ApplicationUser(), "eu@exemplo.com", LinkComoOIdentityEntrega);

        Assert.Contains("Se não foi você", caixa.Ultima!.CorpoHtml);
    }

    [Fact]
    public async Task O_codigo_de_redefinicao_e_escapado()
    {
        // O código vem do Identity e não de um usuário, mas ele entra num HTML — e "vem de lugar
        // confiável" é exatamente a premissa que envelhece mal.
        var (email, caixa) = Montar();
        await email.SendPasswordResetCodeAsync(new ApplicationUser(), "eu@exemplo.com", "<b>123</b>");

        Assert.Contains("&lt;b&gt;123&lt;/b&gt;", caixa.Ultima!.CorpoHtml);
    }

    [Fact]
    public async Task A_confirmacao_de_cadastro_leva_o_proprio_link()
    {
        var (email, caixa) = Montar();
        await email.SendConfirmationLinkAsync(new ApplicationUser(), "eu@exemplo.com", LinkComoOIdentityEntrega);

        Assert.Contains("Confirme seu e-mail", caixa.Ultima!.Assunto);
        Assert.Contains($"href=\"{LinkComoOIdentityEntrega}\"", caixa.Ultima.CorpoHtml);
    }
}
