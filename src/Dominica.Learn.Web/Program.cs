using System.Threading.RateLimiting;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Infrastructure;
using Dominica.Learn.Infrastructure.Indice;
using Dominica.Learn.Infrastructure.Registro;
using Dominica.Learn.Infrastructure.Vault;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Options;
using Dominica.Learn.Web.Anexos;
using Dominica.Learn.Web.Components;
using Dominica.Learn.Web.Components.Account;
using Dominica.Learn.Web.Data;
using Dominica.Learn.Web.Interop;
using Dominica.Learn.Web.Notas;
using Dominica.Learn.Web.Registro;
using Dominica.Learn.Web.Seguranca;
using Dominica.Learn.Web.Vault;
using Dominica.Learn.Web.Tema;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// —— APRESENTAÇÃO ————————————————————————————————————————————————————————————————————
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<IEditorDeTexto, EditorCodeMirror>();
builder.Services.AddScoped<IQuadroDeTinta, QuadroDeTintaJs>();
builder.Services.AddScoped<IRenderizadorDoCliente, RenderizadorDoCliente>();
// Escopo de circuito: o tema é de quem está com a aba aberta, não do servidor.
builder.Services.AddScoped<EstadoDoTema>();
// O cronômetro de estudo: escopo de CIRCUITO, para o tempo sobreviver à navegação. Ver EstadoDoCronometro.
builder.Services.AddScoped<Dominica.Learn.Web.Estudo.EstadoDoCronometro>();
// A ÚNICA tradução de "existe cookie válido" para "este vault é do fulano". Tudo o mais recebe apelido.
builder.Services.AddScoped<ApelidoDeQuemEntrou>();
builder.Services.AddScoped<IUsuarioAtual, UsuarioAtualDoCircuito>();
builder.Services.AddScoped<IPreferenciasDoUsuario, PreferenciasNoIdentity>();

// —— ACOMPANHAR OS ESTUDOS DE OUTRA PESSOA ————————————————————————————————————————————
// A permissão mora no banco da identidade (ver AcompanhamentoNoBanco: a chave não pode ficar do lado
// de dentro da porta que ela abre). A leitura cruzada abre um escopo PRÓPRIO — nunca o do circuito,
// que é a aba de quem está olhando. Ver EstudoDeOutraPessoaEmEscopoProprio.
builder.Services.AddScoped<IAcompanhamentosDeEstudo, AcompanhamentosEmPostgres>();
builder.Services.AddScoped<IEstudoDeOutraPessoa, EstudoDeOutraPessoaEmEscopoProprio>();
builder.Services.AddScoped<Dominica.Learn.Application.CasosDeUso.ServicoDeAcompanhamento>();

// —— IDENTIDADE ——————————————————————————————————————————————————————————————————————
// Banco SEPARADO do índice de propósito: identidade não é conhecimento do usuário e não pode ser
// arrastada por um "reindexar do zero". Compartilham o servidor Postgres, nunca o esquema.
var conexaoIdentidade = builder.Configuration.GetConnectionString("Identidade")
    ?? throw new InvalidOperationException("Falta a cadeia de conexão \"Identidade\" (ConnectionStrings__Identidade).");

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = IdentityConstants.ApplicationScheme;
        o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// Explícito porque o endpoint de anexos usa RequireAuthorization(): as páginas Blazor se protegem pelo
// AuthorizeRouteView, que não passa pelo middleware — sem isto, a proteção do endpoint não teria política
// para aplicar.
builder.Services.AddAuthorization();

// —— HOSPEDAGEM ——————————————————————————————————————————————————————————————————————
// Vazio = raiz do domínio (subdomínio). Preenchido = sob um caminho, atrás do proxy da plataforma.
var hospedagem = builder.Configuration.GetSection(OpcoesDeHospedagem.Secao).Get<OpcoesDeHospedagem>()
    ?? new OpcoesDeHospedagem();
builder.Services.AddSingleton(hospedagem);

// —— ADMINISTRAÇÃO ——————————————————————————————————————————————————————————————————
// O admin mestre é a MESMA PESSOA da Dominica, com CONTA daqui: o Learn não consulta o emissor de
// identidade da plataforma para funcionar. Ver OpcoesDeAdministracao.
builder.Services.AddOptions<OpcoesDeAdministracao>()
    .Bind(builder.Configuration.GetSection(OpcoesDeAdministracao.Secao));

var administracao = builder.Configuration.GetSection(OpcoesDeAdministracao.Secao).Get<OpcoesDeAdministracao>()
    ?? new OpcoesDeAdministracao();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(OpcoesDeAdministracao.Politica, p => p.RequireAssertion(ctx => administracao.EhAdminMestre(ctx.User)));

// FÁBRICA + CONTEXTO POR ESCOPO, e não só o contexto — a tensão clássica entre Identity e Blazor Server.
//
// O Identity exige um ApplicationDbContext scoped; no Blazor Server "scoped" é o CIRCUITO inteiro, e um
// contexto que vive a aba toda é onde nascem as "second operation started on this context" — duas telas
// consultando ao mesmo tempo. O resto do Learn já resolveu isso com fábrica (ver ContextoDoRegistro e
// IndiceEmPostgres): cada operação abre o seu, curto e descartável.
//
// Registrar a fábrica e derivar o scoped DELA dá os dois sem duplicar configuração: o Identity continua
// recebendo o contexto que espera, e quem lê acompanhamento pede a fábrica, como todo o resto faz.
builder.Services.AddDbContextFactory<ApplicationDbContext>(o => o.UseNpgsql(conexaoIdentidade));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        // Em uso familiar (eu, meu irmão, amigos) não há servidor de e-mail, e exigir confirmação
        // deixaria todo mundo trancado do lado de fora. É configurável para o dia em que abrir ao público.
        o.SignIn.RequireConfirmedAccount = builder.Configuration.GetValue("Identidade:ExigirEmailConfirmado", false);
        // FRASE-SENHA EM VEZ DE TEATRO DE CARACTERES ESPECIAIS. Só aumentar RequiredLength não basta: as
        // outras regras do Identity continuam ligadas por padrão, e juntas recusam "uma frase senha longa"
        // — que é mais forte que "Senha@123" e é a que alguém consegue lembrar todo dia. Comprimento é o
        // que de fato custa a um ataque; classe de caractere só empurra o usuário para o post-it.
        o.Password.RequiredLength = 12;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireDigit = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequiredUniqueChars = 4;   // barra "aaaaaaaaaaaa", que passa por comprimento
        o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// O Identity manda e-mail pela porta do app, e o adaptador concreto (SMTP ou log) é escolhido lá na
// infraestrutura, pela presença da configuração. Ver EmailDoIdentity e RegistroDaInfraestrutura.
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, EmailDoIdentity>();

// —— COOKIE ——————————————————————————————————————————————————————————————————————————
builder.Services.ConfigureApplicationCookie(o =>
{
    // Nome e caminho PRÓPRIOS: sob um sub-caminho o Learn divide domínio com a plataforma, e dois apps
    // com cookie de mesmo nome se derrubam. O sintoma seria "fui deslogado sozinho" — que ninguém liga
    // à causa. O Path restringe o envio ao que é do Learn, o que também é menos cookie viajando à toa.
    o.Cookie.Name = hospedagem.NomeDoCookie;
    o.Cookie.Path = hospedagem.BaseHref;
    o.Cookie.HttpOnly = true;                                   // fora do alcance de qualquer script
    o.Cookie.SameSite = SameSiteMode.Strict;                    // CSRF: o cookie não viaja em requisição de terceiro
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;          // só por HTTPS — o proxy reverso termina TLS
    o.ExpireTimeSpan = TimeSpan.FromDays(30);                   // estudo é diário: relogar toda hora é atrito puro
    o.SlidingExpiration = true;
});

// —— APLICAÇÃO + INFRAESTRUTURA ————————————————————————————————————————————————————
builder.Services.AdicionarInfraestruturaDoLearn(builder.Configuration);

// —— LIMITE DE REQUISIÇÕES ——————————————————————————————————————————————————————————
// Protege o login de força bruta, e a prévia de hover de martelo. O resto do app é Blazor Server (um
// circuito WebSocket por sessão), que não se defende com contagem de requisições HTTP — por isso o
// limite é aplicado só onde ele serve, em vez de globalmente, onde daria falsa sensação de proteção.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("autenticacao", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    // A PRÉVIA É HTTP FORA DO CIRCUITO — a exceção à regra acima. Cada acerto renderiza uma nota do
    // disco, com as transclusões dela; sem teto, um script segurando o mouse renderiza o vault em loop.
    // 120/min é dez vezes o que uma leitura de verdade produz, então usuário nenhum encosta no limite.
    o.AddPolicy("previa", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

// —— OBSERVABILIDADE ————————————————————————————————————————————————————————————————
// Ligado desde o começo, mesmo sem coletor configurado: instrumentar depois significa instrumentar no
// meio de um incidente, que é quando menos se consegue pensar.
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("dominica-learn"))
    // O rastreamento do Npgsql fica de fora por ora: a extensão AddNpgsql do pacote de OpenTelemetry
    // colide, na resolução de sobrecarga, com a homônima do EF Core, e resolver isso com nome totalmente
    // qualificado deixaria uma linha ilegível para ganhar traço de banco que ninguém coleta ainda. Volta
    // junto com o coletor, numa linha só.
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddRuntimeInstrumentation());

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ContextoDoIndice>("indice")
    .AddCheck<SaudeDoVault>("vault");

var app = builder.Build();

// —— MIGRAÇÕES ——————————————————————————————————————————————————————————————————————
// Aplicadas na subida: em VPS de um usuário só, o passo manual de migração é o passo que se esquece.
using (var escopo = app.Services.CreateScope())
{
    await escopo.ServiceProvider.GetRequiredService<ContextoDoIndice>().Database.MigrateAsync();
    await escopo.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    await escopo.ServiceProvider.GetRequiredService<ContextoDoRegistro>().Database.MigrateAsync();

    // —— E O DISCO ——————————————————————————————————————————————————————————————————
    // O que estava em {raiz}/{apelido} desce para {raiz}/{apelido}/estudo, e daí em diante cada pasta
    // ali é um vault. Roda a TODA subida porque ela sabe não fazer nada — ver as guardas em
    // MigracaoParaVaults. Um passo manual no dia do deploy é o passo que se esquece, e esquecer este
    // deixa o vault de alguém vazio na tela.
    //
    // DEPOIS DAS MIGRAÇÕES DE BANCO, e a ordem é o que evita a janela ruim: as linhas antigas do índice
    // e do registro já foram carimbadas com "estudo" quando os arquivos chegam lá.
    //
    // ANTES DE O APP ATENDER, porque o vigia começa a reconciliar assim que sobe: se ele passasse pelo
    // disco no meio da mudança, veria metade das notas e apagaria a outra metade do índice.
    var opcoesDoVault = escopo.ServiceProvider.GetRequiredService<IOptions<OpcoesDoVault>>().Value;
    MigracaoParaVaults.MigrarTodos(
        opcoesDoVault.Raiz,
        NomeDoVault.Padrao,
        escopo.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MigracaoParaVaults"));
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// PathBase ANTES de tudo: daqui para baixo o app enxerga "/notas" mesmo quando a URL é
// "/private/dominica-learn/notas". As rotas das páginas continuam escritas sem o prefixo.
// —— O PIPELINE, NA ORDEM, E POR INTEIRO ————————————————————————————————————————————
//
// Está todo explícito de propósito. O WebApplication insere roteamento, autenticação e autorização
// sozinho quando não são declarados — e a posição que ele escolhe deixa de servir assim que
// UsePathBase entra em jogo. Foram dois defeitos seguidos por causa disso, ambos só visíveis rodando:
//
//   1. sem UseRouting explícito, o GET funcionava sob o sub-caminho e o POST do formulário dava 405,
//      porque a rota era casada com o prefixo ainda no caminho;
//   2. com UseRouting explícito e o resto implícito, a autorização passou a rodar ANTES do roteamento,
//      e toda página com [Authorize] quebrava com "no middleware that supports authorization".
//
// Declarar os sete na ordem custa sete linhas e tira o palpite da jogada.
if (hospedagem.CaminhoBase.Length > 0) app.UsePathBase(hospedagem.CaminhoBase);

// AS BIBLIOTECAS DE `lib/` SÃO IMUTÁVEIS, E PRECISAM DIZER ISSO.
//
// Sem cabeçalho de cache elas são REVALIDADAS a cada navegação: uma ida e volta por arquivo, cinco
// arquivos do CodeMirror. Em localhost não custa nada; com 300 ms de latência é ~1,5 s toda vez que se
// abre uma nota, MESMO com tudo já baixado. Medido.
//
// O carimbo vai no `OnStarting` — no instante em que a resposta sai — e não nas opções do
// UseStaticFiles, porque quem de fato serve estes arquivos é o MapStaticAssets (a ETag com hash e o
// `Vary: Accept-Encoding` na resposta o entregam), e ele já tinha posto `no-cache` ali. Descoberto
// olhando o cabeçalho de verdade depois de a primeira tentativa não mudar nada.
//
// `immutable` é honesto aqui e não seria em outro lugar: são cópias versionadas de terceiros, e elas
// só mudam quando alguém troca o arquivo e faz deploy — e o deploy troca a imagem inteira.
app.Use(async (ctx, seguinte) =>
{
    if (ctx.Request.Path.StartsWithSegments("/lib"))
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Task.CompletedTask;
        });
    await seguinte();
});

app.UseHttpsRedirection();
app.UseCabecalhosDeSeguranca();   // CSP, X-Content-Type-Options, Referrer-Policy — ver Seguranca/
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();

app.MapHealthChecks("/saude");
app.MapearAnexos();               // /anexos/** — autenticado, lista de permissão, sem sair da raiz
app.MapearPrevia();               // /previa/** — o fragmento que o hover de wikilink mostra
app.MapearPacoteDoVault();        // /vault.zip — o download do vault inteiro, fora do circuito
app.MapearExportacaoAnki();       // /anki.txt — os cartões no formato de importação do Anki
app.MapearDownloadDaNota();       // /nota.md?caminho=… — UMA nota, para mandar a alguém ou abrir fora
app.MapearRegistroEmCsv();        // /sessoes.csv — o histórico do cronômetro em planilha
app.MapearTrocaDeVault();         // /vault/trocar — o alternador da barra, que é estática
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();

app.Run();
