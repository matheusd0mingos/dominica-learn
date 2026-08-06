using System.Threading.RateLimiting;
using Dominica.Learn.Infrastructure;
using Dominica.Learn.Infrastructure.Indice;
using Dominica.Learn.Web.Components;
using Dominica.Learn.Web.Components.Account;
using Dominica.Learn.Web.Data;
using Dominica.Learn.Web.Interop;
using Dominica.Learn.Web.Seguranca;
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

builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(conexaoIdentidade));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        // Em uso familiar (eu, meu irmão, amigos) não há servidor de e-mail, e exigir confirmação
        // deixaria todo mundo trancado do lado de fora. É configurável para o dia em que abrir ao público.
        o.SignIn.RequireConfirmedAccount = builder.Configuration.GetValue("Identidade:ExigirEmailConfirmado", false);
        o.Password.RequiredLength = 12;   // frase-senha em vez de teatro de caracteres especiais
        o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// —— COOKIE ——————————————————————————————————————————————————————————————————————————
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.HttpOnly = true;                                   // fora do alcance de qualquer script
    o.Cookie.SameSite = SameSiteMode.Strict;                    // CSRF: o cookie não viaja em requisição de terceiro
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;          // só por HTTPS — o proxy reverso termina TLS
    o.ExpireTimeSpan = TimeSpan.FromDays(30);                   // estudo é diário: relogar toda hora é atrito puro
    o.SlidingExpiration = true;
});

// —— APLICAÇÃO + INFRAESTRUTURA ————————————————————————————————————————————————————
builder.Services.AdicionarInfraestruturaDoLearn(builder.Configuration);

// —— LIMITE DE REQUISIÇÕES ——————————————————————————————————————————————————————————
// Protege o login de força bruta. O resto do app é Blazor Server (um circuito WebSocket por sessão), que
// não se defende com contagem de requisições HTTP — por isso o limite é aplicado só onde ele serve, em
// vez de globalmente, onde daria falsa sensação de proteção.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("autenticacao", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
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

app.UseHttpsRedirection();
app.UseCabecalhosDeSeguranca();   // CSP, X-Content-Type-Options, Referrer-Policy — ver Seguranca/
app.UseStaticFiles();
app.UseAntiforgery();
app.UseRateLimiter();

app.MapHealthChecks("/saude");
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();

app.Run();
