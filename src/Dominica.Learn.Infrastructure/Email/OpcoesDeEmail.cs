using System.ComponentModel.DataAnnotations;

namespace Dominica.Learn.Infrastructure.Email;

/// <summary>
/// Configuração do envio de e-mail.
///
/// OS MESMOS DADOS DA PLATAFORMA, e de propósito: quem hospeda o Learn ao lado da Dominica tem uma
/// caixa de saída só, e manter duas cópias das mesmas credenciais é garantir que um dia elas divirjam.
/// O compose alimenta esta seção a partir de PLATAFORMA_SMTP_* do .env — ver docker-compose.learn.yml.
/// A tradução acontece LÁ, e não aqui, porque o Learn também roda sozinho, e um app não deve carregar
/// no código o nome de outro produto.
///
/// SEM <c>Host</c>, NÃO HÁ ENVIO — e isso não é falha, é o padrão. Ver EmailQueSoRegistra.
/// </summary>
public sealed class OpcoesDeEmail
{
    public const string Secao = "Email";

    /// <summary>Servidor SMTP. Vazio desliga o envio.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Porta. 587 (STARTTLS) é a que funciona aqui.
    ///
    /// NÃO USE 465. Aquela porta espera TLS implícito — a conexão já começa criptografada — e o
    /// <c>System.Net.Mail.SmtpClient</c> não faz isso: ele conecta em claro e negocia STARTTLS depois.
    /// Apontar para 465 dá um travamento sem mensagem útil, e o palpite natural ("deve ser a senha")
    /// leva a tarde inteira para a direção errada.
    /// </summary>
    [Range(1, 65535)]
    public int Porta { get; set; } = 587;

    /// <summary>Caixa autenticada. Vazio = servidor que não pede autenticação (raro fora da rede local).</summary>
    public string? Usuario { get; set; }

    /// <summary>
    /// Senha da caixa. Com verificação em duas etapas ligada, é a SENHA DE APLICATIVO — a senha de
    /// login é recusada, e o servidor responde "authentication failed" sem dizer por quê.
    /// </summary>
    public string? Senha { get; set; }

    /// <summary>
    /// Remetente. Tem de ser a própria caixa autenticada ou um alias que ela possua: qualquer outro
    /// endereço é recusado pelo servidor, ou aceito e entregue direto no spam de quem recebe.
    /// </summary>
    public string Remetente { get; set; } = string.Empty;

    /// <summary>Nome que aparece antes do endereço na caixa de quem recebe.</summary>
    public string NomeDoRemetente { get; set; } = "Dominica Learn";

    /// <summary>STARTTLS. Só desligue em servidor de teste dentro da própria máquina.</summary>
    public bool Ssl { get; set; } = true;

    /// <summary>
    /// Há envio configurado. Exige as duas coisas: sem remetente o servidor recusa a mensagem, e ter
    /// host sem remetente é uma configuração pela metade que falharia só na hora de mandar — quando
    /// alguém já está esperando o e-mail. Ver a validação em RegistroDaInfraestrutura.
    /// </summary>
    public bool Ligado => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Remetente);
}
