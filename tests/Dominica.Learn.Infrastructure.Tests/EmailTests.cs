using Dominica.Learn.Application.Portas;
using Dominica.Learn.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Infrastructure.Tests;

// O QUE ESTES TESTES PROTEGEM: a resposta à pergunta "este servidor manda e-mail?".
//
// Ela não é cosmética. Três telas mudam de texto com ela — a recuperação de senha diz se a mensagem vai
// chegar ou não, e o Login mostra ou esconde o "esqueci minha frase". Se a resposta errar, o app passa a
// mentir para quem está trancado do lado de fora, que é exatamente quem menos pode ser enganado.
public class EmailTests
{
    private static OpcoesDeEmail Opcoes(string host = "", string remetente = "") =>
        new() { Host = host, Remetente = remetente };

    [Fact]
    public void Sem_host_o_email_esta_desligado()
    {
        Assert.False(Opcoes(remetente: "nao-responda@exemplo.com").Ligado);
    }

    [Fact]
    public void Host_sem_remetente_tambem_conta_como_desligado()
    {
        // Configuração pela metade não é "meio ligada": o servidor recusaria a mensagem. Melhor o app
        // dizer que não manda do que tentar e falhar com alguém esperando.
        Assert.False(Opcoes(host: "smtp.exemplo.com").Ligado);
    }

    [Fact]
    public void Com_host_e_remetente_esta_ligado()
    {
        Assert.True(Opcoes("smtp.exemplo.com", "nao-responda@exemplo.com").Ligado);
    }

    // —— A ESCOLHA DO ADAPTADOR, que é onde o defeito real moraria ——————————————————————————

    private static IEnviadorDeEmail Enviador(params (string chave, string valor)[] config)
    {
        var servicos = new ServiceCollection();
        servicos.AddLogging();
        servicos.AddOptions<OpcoesDeEmail>().Bind(
            new ConfigurationBuilder()
                .AddInMemoryCollection(config.Select(c => new KeyValuePair<string, string?>(c.chave, c.valor)))
                .Build()
                .GetSection(OpcoesDeEmail.Secao));

        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(config.Select(c => new KeyValuePair<string, string?>(c.chave, c.valor)))
            .Build();

        if (!string.IsNullOrWhiteSpace(configuracao[$"{OpcoesDeEmail.Secao}:Host"]))
            servicos.AddSingleton<IEnviadorDeEmail, EmailPorSmtp>();
        else
            servicos.AddSingleton<IEnviadorDeEmail>(sp => new EmailQueSoRegistra(
                sp.GetRequiredService<ILogger<EmailQueSoRegistra>>(), escreverOCorpo: false));

        return servicos.BuildServiceProvider().GetRequiredService<IEnviadorDeEmail>();
    }

    [Fact]
    public void Sem_configuracao_nenhuma_o_adaptador_e_o_que_so_registra()
    {
        var e = Enviador();
        Assert.IsType<EmailQueSoRegistra>(e);
        Assert.False(e.Ligado);
    }

    [Fact]
    public void Com_host_configurado_o_adaptador_e_o_smtp()
    {
        var e = Enviador(("Email:Host", "smtp.exemplo.com"), ("Email:Remetente", "nao-responda@exemplo.com"));
        Assert.IsType<EmailPorSmtp>(e);
        Assert.True(e.Ligado);
    }

    [Fact]
    public async Task O_adaptador_de_log_nao_lanca_e_nao_pretende_ter_enviado()
    {
        // Ele resolve a promessa — quem chama não quebra —, mas continua dizendo que está desligado.
        // É a combinação exata: o cadastro funciona sem servidor de e-mail, e a tela sabe disso.
        var e = Enviador();
        await e.EnviarAsync(new MensagemDeEmail("alguem@exemplo.com", "Assunto", "<p>corpo</p>"));
        Assert.False(e.Ligado);
    }

    // —— A VALIDAÇÃO QUE DERRUBA A SUBIDA ————————————————————————————————————————————————

    [Fact]
    public void Host_sem_remetente_nao_deixa_o_app_subir()
    {
        // A metade-configuração falharia só no primeiro envio, semanas depois, com alguém esperando o
        // e-mail. Aqui ela falha no deploy, que é quando ainda há quem conserte.
        var servicos = new ServiceCollection();
        servicos.AddOptions<OpcoesDeEmail>()
            .Configure(o => { o.Host = "smtp.exemplo.com"; o.Remetente = ""; })
            .Validate(o => string.IsNullOrWhiteSpace(o.Host) || !string.IsNullOrWhiteSpace(o.Remetente),
                "Email:Host preenchido sem Email:Remetente.");

        var opcoes = servicos.BuildServiceProvider().GetRequiredService<IOptions<OpcoesDeEmail>>();
        var erro = Assert.Throws<OptionsValidationException>(() => opcoes.Value);
        Assert.Contains("Remetente", erro.Message);
    }
}
