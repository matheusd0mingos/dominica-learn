using System.Net;
using System.Net.Mail;
using System.Text;
using Dominica.Learn.Application.Portas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Infrastructure.Email;

/// <summary>
/// Envio por SMTP. Adaptador de <see cref="IEnviadorDeEmail"/> para produção.
///
/// MESMA RECEITA DA PLATAFORMA — <c>System.Net.Mail</c>, sem biblioteca a mais. Não é falta de opção
/// melhor: é que este app manda três tipos de mensagem, todas com um link dentro, e a única coisa que
/// uma dependência nova traria aqui é mais uma peça para atualizar em cada release do .NET. Se um dia
/// for preciso anexo, DKIM próprio ou fila de reenvio, esta classe é o lugar de trocar — e nada fora
/// dela precisa saber.
///
/// O CLIENTE É CRIADO POR MENSAGEM, e não guardado. O <c>SmtpClient</c> não é seguro para uso
/// concorrente, e este serviço é singleton: um cliente compartilhado misturaria dois envios
/// simultâneos. Abrir a conexão custa uma ida ao servidor, e mandar e-mail não está em caminho quente
/// nenhum — o custo é invisível e a alternativa é um defeito que só aparece com duas pessoas ao mesmo
/// tempo, que é quando ninguém está olhando.
/// </summary>
public sealed class EmailPorSmtp(IOptions<OpcoesDeEmail> opcoes, ILogger<EmailPorSmtp> log) : IEnviadorDeEmail
{
    private readonly OpcoesDeEmail _cfg = opcoes.Value;

    public bool Ligado => true;

    public async Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default)
    {
        using var msg = new MailMessage
        {
            From = new MailAddress(_cfg.Remetente, _cfg.NomeDoRemetente, Encoding.UTF8),
            Subject = mensagem.Assunto,
            Body = mensagem.CorpoHtml,
            IsBodyHtml = true,
            // UTF-8 DITO EM VOZ ALTA, nos três lugares. O padrão do MailMessage é ASCII para o
            // cabeçalho, e o assunto deste app tem "Redefinir sua frase secreta" — sem isto, o acento
            // vira lixo na caixa de entrada de quem recebe, e o corpo vem com "frase secre" cortado em
            // clientes mais velhos. É o tipo de defeito que não aparece em teste nenhum escrito em
            // inglês.
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
        };
        msg.To.Add(mensagem.Para);

        using var cliente = new SmtpClient(_cfg.Host, _cfg.Porta) { EnableSsl = _cfg.Ssl };
        if (!string.IsNullOrEmpty(_cfg.Usuario))
            cliente.Credentials = new NetworkCredential(_cfg.Usuario, _cfg.Senha);

        try
        {
            await cliente.SendMailAsync(msg, ct);
            // O ASSUNTO ENTRA NO LOG, O CORPO NÃO. O corpo carrega o link de redefinir senha, que é um
            // token de acesso à conta: quem lê o log passaria a poder entrar como a pessoa.
            log.LogInformation("E-mail enviado para {Destino}: {Assunto}", mensagem.Para, mensagem.Assunto);
        }
        catch (Exception e)
        {
            // REGISTRA E RELANÇA. Registrar sem relançar diria à tela que o e-mail saiu, e a pessoa
            // esperaria por uma mensagem que nunca existiu. Quem chama decide o que dizer ao usuário;
            // aqui o dever é não mentir.
            log.LogError(e, "Falhou o envio para {Destino} via {Host}:{Porta}", mensagem.Para, _cfg.Host, _cfg.Porta);
            throw;
        }
    }
}
