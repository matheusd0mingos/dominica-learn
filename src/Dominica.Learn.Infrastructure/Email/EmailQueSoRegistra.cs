using Dominica.Learn.Application.Portas;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Infrastructure.Email;

/// <summary>
/// O adaptador de quando não há servidor de e-mail configurado: escreve a mensagem no log e pronto.
///
/// ELE DIZ QUE ESTÁ DESLIGADO — <see cref="Ligado"/> é <c>false</c> — e essa honestidade é o recurso
/// inteiro. Com ela, a tela de recuperação de senha avisa que nenhuma mensagem vai chegar em vez de
/// mandar a pessoa conferir a caixa de entrada pelo resto do dia. Um no-op silencioso que se passa por
/// funcionando é a pior das três opções possíveis aqui.
///
/// EM DESENVOLVIMENTO ELE É ÚTIL DE VERDADE: o link de confirmação sai inteiro no log, e dá para
/// colar no navegador e seguir o fluxo sem montar servidor de e-mail nenhum. Em produção isso seria
/// um vazamento — o link de redefinir senha é um token de acesso à conta —, então o corpo só é
/// escrito em Development. Ver a decisão em RegistroDaInfraestrutura.
/// </summary>
public sealed class EmailQueSoRegistra(ILogger<EmailQueSoRegistra> log, bool escreverOCorpo) : IEnviadorDeEmail
{
    public bool Ligado => false;

    public Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default)
    {
        if (escreverOCorpo)
            log.LogWarning("Sem servidor de e-mail. A mensagem para {Destino} ({Assunto}) NÃO foi enviada:\n{Corpo}",
                mensagem.Para, mensagem.Assunto, mensagem.CorpoHtml);
        else
            log.LogWarning("Sem servidor de e-mail. A mensagem para {Destino} ({Assunto}) NÃO foi enviada.",
                mensagem.Para, mensagem.Assunto);

        return Task.CompletedTask;
    }
}
