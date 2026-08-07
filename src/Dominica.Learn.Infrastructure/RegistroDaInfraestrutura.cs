using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Infrastructure.Email;
using Dominica.Learn.Infrastructure.Indice;
using Dominica.Learn.Infrastructure.Registro;
using Dominica.Learn.Infrastructure.Renderizacao;
using Dominica.Learn.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Infrastructure;

/// <summary>Relógio de verdade. Único lugar do sistema autorizado a perguntar as horas ao sistema.</summary>
public sealed class RelogioDoSistema : IRelogio
{
    public DateTimeOffset Agora => DateTimeOffset.UtcNow;
}

/// <summary>
/// O ÚNICO ponto onde as portas do domínio encontram os adaptadores concretos.
///
/// Esta classe é o teste vivo da arquitetura hexagonal: se um dia trocar Postgres por SQLite ou disco por
/// S3 exigir mudar algo fora daqui, o acoplamento vazou e alguém referenciou infraestrutura de onde não
/// devia. Manter este arquivo pequeno é manter a arquitetura honesta.
/// </summary>
public static class RegistroDaInfraestrutura
{
    public static IServiceCollection AdicionarInfraestruturaDoLearn(
        this IServiceCollection servicos, IConfiguration configuracao)
    {
        servicos.AddOptions<OpcoesDoVault>()
            .Bind(configuracao.GetSection(OpcoesDoVault.Secao))
            .ValidateDataAnnotations()
            // Valida NA INICIALIZAÇÃO, não no primeiro uso: descobrir que Vault:Raiz está vazio quando o
            // usuário clica em "abrir nota" é descobrir tarde. Melhor o contêiner não subir.
            .ValidateOnStart();

        var conexao = configuracao.GetConnectionString("Indice")
            ?? throw new InvalidOperationException(
                "Falta a cadeia de conexão \"Indice\". Defina ConnectionStrings__Indice no ambiente ou em appsettings.");

        // POR FÁBRICA, e não AddDbContext. No Blazor Server o escopo é o CIRCUITO: um contexto para a
        // aba inteira, vivo por horas. Duas operações sobrepostas nele fazem o EF lançar "a second
        // operation was started on this context instance"; a exceção sobe pela renderização e MATA O
        // CIRCUITO — a tela fica desenhada, o autosave para de chegar, e o que se digita depois some sem
        // aviso. Aconteceu, e custou uma sessão de depuração. Ver IndiceEmPostgres.AbrirAsync.
        //
        // Quem recebe o contexto DIRETO — a migração na subida e o health check — continua recebendo,
        // por um registro escopado que DELEGA À FÁBRICA. Registrar AddDbContext ao lado não funciona: a
        // fábrica é singleton e as opções do AddDbContext são escopadas, e o boot morre com "cannot
        // resolve scoped service ... from root provider". Custou um contêiner que não subia para
        // aprender, e é por isso que está escrito aqui.
        servicos.AddDbContextFactory<ContextoDoIndice>(o => o.UseNpgsql(conexao));
        servicos.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ContextoDoIndice>>().CreateDbContext());

        // O REGISTRO TEM BANCO PRÓPRIO, e a razão está no nome dele. O índice é descartável — a
        // documentação manda apagá-lo e reindexar quando algo está estranho. Horas estudadas e questões
        // resolvidas não são reconstruíveis a partir de nada; apagadas, acabaram. Bancos separados põem
        // essa diferença onde ninguém precisa lembrar dela.
        //
        // Cai no banco de identidade quando não há cadeia própria: identidade também é durável, e é
        // melhor conviver com identidade do que estrear no banco que a documentação manda apagar.
        var conexaoDoRegistro = configuracao.GetConnectionString("Registro")
            ?? configuracao.GetConnectionString("Identidade")
            ?? throw new InvalidOperationException(
                "Falta a cadeia de conexão \"Registro\" (ou, na falta dela, \"Identidade\").");

        servicos.AddDbContextFactory<ContextoDoRegistro>(o => o.UseNpgsql(conexaoDoRegistro));
        servicos.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ContextoDoRegistro>>().CreateDbContext());

        // —— E-MAIL ——————————————————————————————————————————————————————————————————————
        //
        // LIGADO PELA PRESENÇA DA CONFIGURAÇÃO, não por uma chave "Email:Ativo". Uma chave dessas
        // permite o estado sem sentido — ativo sem host — e obriga quem configura a acertar duas coisas
        // em vez de uma. Preencheu o servidor, manda; não preencheu, não manda e DIZ que não manda.
        servicos.AddOptions<OpcoesDeEmail>()
            .Bind(configuracao.GetSection(OpcoesDeEmail.Secao))
            .ValidateDataAnnotations()
            // HOST SEM REMETENTE NÃO SOBE. É a configuração pela metade — o servidor recusaria a
            // mensagem —, e o momento de descobrir isso é o deploy, não a primeira pessoa que clicar
            // em "esqueci minha senha" seis semanas depois.
            .Validate(o => string.IsNullOrWhiteSpace(o.Host) || !string.IsNullOrWhiteSpace(o.Remetente),
                "Email:Host está preenchido mas Email:Remetente não. Sem remetente o servidor recusa a mensagem.")
            .ValidateOnStart();

        var emailConfigurado = !string.IsNullOrWhiteSpace(configuracao[$"{OpcoesDeEmail.Secao}:Host"]);
        var ehDesenvolvimento = string.Equals(
            configuracao["ASPNETCORE_ENVIRONMENT"] ?? configuracao["DOTNET_ENVIRONMENT"],
            "Development", StringComparison.OrdinalIgnoreCase);

        if (emailConfigurado)
            servicos.AddSingleton<IEnviadorDeEmail, EmailPorSmtp>();
        else
            // O corpo só vai para o log em desenvolvimento: em produção ele carregaria o link de
            // redefinir senha, que é um token de acesso à conta. Ver EmailQueSoRegistra.
            servicos.AddSingleton<IEnviadorDeEmail>(sp => new EmailQueSoRegistra(
                sp.GetRequiredService<ILogger<EmailQueSoRegistra>>(), ehDesenvolvimento));

        servicos.AddSingleton<IRelogio, RelogioDoSistema>();
        // Singleton: o pipeline do Markdig é imutável e caro de montar; recriá-lo por requisição seria
        // pagar a construção a cada abertura de nota.
        servicos.AddSingleton<IRenderizadorDeMarkdown, RenderizadorMarkdig>();
        // Escopo de circuito (ou o escopo que o vigia abre): dentro dele o usuário não muda.
        servicos.AddScoped<EscopoDoUsuario>();
        servicos.AddScoped<RaizDoVaultDoUsuario>();
        servicos.AddScoped<IRepositorioDeNotas, RepositorioDeNotasEmDisco>();
        servicos.AddScoped<IArmazemDeAnexos, ArmazemDeAnexosEmDisco>();
        servicos.AddScoped<IEmpacotadorDoVault, EmpacotadorEmZip>();
        servicos.AddScoped<IIndiceDoVault, IndiceEmPostgres>();
        servicos.AddScoped<IHistoricoDeNotas, HistoricoEmPostgres>();
        servicos.AddScoped<IRegistroDeEstudo, RegistroEmPostgres>();

        servicos.AddScoped<ReconciliarVault>();
        servicos.AddScoped<ServicoDeNotas>();
        servicos.AddScoped<ServicoDeConhecimento>();
        servicos.AddScoped<ServicoDeAnexos>();
        servicos.AddScoped<ServicoDeCartoes>();
        servicos.AddScoped<ServicoDeDesempenho>();

        servicos.AddHostedService<VigiaDoVault>();

        return servicos;
    }
}
