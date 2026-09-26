namespace Dominica.Learn.Application.Portas;

/// <summary>
/// Uma mensagem a enviar. O corpo é HTML porque tudo que este app manda é um link para clicar, e um
/// link cru no meio de texto puro é o tipo de coisa que o cliente de e-mail quebra em duas linhas e o
/// usuário não consegue copiar inteiro.
/// </summary>
public sealed record MensagemDeEmail(string Para, string Assunto, string CorpoHtml);

/// <summary>
/// Porta de envio de e-mail.
///
/// EXISTE PARA QUE O RESTO DO SISTEMA NÃO SAIBA O QUE É SMTP. Quem manda um link de confirmação não
/// deveria precisar conhecer host, porta, STARTTLS nem senha de aplicativo — e, principalmente, não
/// deveria mudar no dia em que isso virar uma API HTTP de um serviço de envio.
///
/// A PORTA NÃO PROMETE ENTREGA. Ela promete ter tentado, e falhar alto quando não conseguiu. Servidor
/// de e-mail recusa, atrasa e engole mensagem por motivos que não são do nosso alcance; um adaptador
/// que engula a exceção transforma "não chegou" em "não aconteceu nada", que é o defeito mais caro de
/// diagnosticar que existe nesta área.
/// </summary>
public interface IEnviadorDeEmail
{
    /// <summary>
    /// Diz se há envio de verdade configurado.
    ///
    /// ISTO É PARTE DO CONTRATO, e não um detalhe do adaptador, porque a INTERFACE DO APP MUDA com a
    /// resposta: sem envio, a tela de recuperação de senha precisa dizer que nenhuma mensagem vai
    /// chegar, em vez de mandar a pessoa esperar para sempre. Uma tela que mente sobre isso é pior que
    /// uma tela que não existe.
    /// </summary>
    bool Ligado { get; }

    Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default);
}
