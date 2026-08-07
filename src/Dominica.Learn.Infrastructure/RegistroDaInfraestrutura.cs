using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Infrastructure.Indice;
using Dominica.Learn.Infrastructure.Renderizacao;
using Dominica.Learn.Infrastructure.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        servicos.AddDbContext<ContextoDoIndice>(o => o.UseNpgsql(conexao));

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

        servicos.AddScoped<ReconciliarVault>();
        servicos.AddScoped<ServicoDeNotas>();
        servicos.AddScoped<ServicoDeConhecimento>();
        servicos.AddScoped<ServicoDeAnexos>();
        servicos.AddScoped<ServicoDeCartoes>();

        servicos.AddHostedService<VigiaDoVault>();

        return servicos;
    }
}
