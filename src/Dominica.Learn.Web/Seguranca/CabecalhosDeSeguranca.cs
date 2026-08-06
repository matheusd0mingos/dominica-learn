using Dominica.Learn.Infrastructure.Vault;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// Cabeçalhos de segurança em toda resposta.
///
/// A CSP é a peça que mais importa num produto cujo conteúdo é MARKDOWN ESCRITO PELO USUÁRIO e colado de
/// qualquer lugar da internet. Um resumo copiado de um site pode trazer `&lt;img onerror=...&gt;` junto; o
/// renderizador tem de sanitizar, mas confiar só nele é apostar tudo numa única linha de defesa. A CSP é
/// a segunda linha, e é a que continua de pé quando a primeira falha.
/// </summary>
public static class CabecalhosDeSeguranca
{
    public static IApplicationBuilder UseCabecalhosDeSeguranca(this IApplicationBuilder app) =>
        app.Use(async (ctx, proximo) =>
        {
            var h = ctx.Response.Headers;

            // 'unsafe-inline' em script-src é uma CONCESSÃO CONHECIDA, não descuido: o Blazor Server
            // injeta o blazor.web.js e o estado inicial inline, e sem isso a aplicação não inicia. O
            // caminho para removê-la é nonce por requisição — trabalho real, agendado, não esquecido.
            h["Content-Security-Policy"] = string.Join("; ",
                "default-src 'self'",
                "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'",
                "style-src 'self' 'unsafe-inline'",
                "img-src 'self' data: blob:",
                "font-src 'self' data:",
                "connect-src 'self' ws: wss:",     // o circuito do Blazor Server é WebSocket
                "frame-ancestors 'none'",          // ninguém embute o Learn num iframe
                "base-uri 'self'",
                "form-action 'self'");

            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["X-Frame-Options"] = "DENY";
            // Nenhuma dessas capacidades é usada pelo produto; negá-las por padrão evita que um plugin ou
            // um trecho colado peça acesso a câmera ou localização em nome do usuário.
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";

            await proximo();
        });
}

/// <summary>
/// Verificação de saúde do vault: a pasta existe e dá para escrever nela?
///
/// É a falha mais provável em produção e a mais silenciosa — volume não montado no contêiner, permissão
/// errada depois de um deploy. Sem esta checagem, o app sobe "saudável", aceita o que o usuário escreve
/// e perde tudo na gravação. Health check que só olha o banco daria exatamente essa falsa segurança.
/// </summary>
public sealed class SaudeDoVault(IOptions<OpcoesDoVault> opcoes) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext contexto, CancellationToken ct = default)
    {
        var raiz = opcoes.Value.Raiz;
        try
        {
            if (!Directory.Exists(raiz))
                return Task.FromResult(HealthCheckResult.Unhealthy($"A raiz do vault não existe: {raiz}"));

            var teste = Path.Combine(raiz, $".learn-saude-{Guid.NewGuid():N}");
            File.WriteAllText(teste, "ok");
            File.Delete(teste);
            return Task.FromResult(HealthCheckResult.Healthy($"Vault gravável em {raiz}"));
        }
        catch (Exception e)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Vault não gravável em {raiz}", e));
        }
    }
}
