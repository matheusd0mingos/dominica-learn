using System.Text;
using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace Dominica.Learn.Web.Notas;

/// <summary>
/// As prévias de hover — os fragmentos de HTML que o popover mostra quando o mouse paira num alvo.
///
/// SÃO ENDPOINTS, e não interop do circuito, porque o hover é do navegador: o mouse paira e a resposta
/// precisa vir sem acordar o Blazor — um round-trip de circuito por hover deixaria o ponteiro na frente
/// do popover. Os fetches são feitos por js/previa.js, com URL RELATIVA (respeita o &lt;base href&gt;).
///
/// TRÊS TIPOS DE ALVO, TRÊS PRÉVIAS:
///   • uma NOTA (link ou nó do grafo)     → o HTML dela, renderizado;
///   • uma ETIQUETA (nó do mapa de tags)  → a lista das notas que a têm;
///   • uma MATÉRIA (nó do mapa de matérias) → a lista das notas dela.
/// A da nota é a nota inteira; as outras duas são um ESPIAR do recorte antes de clicar para abrir o
/// painel — que é o que o mapa pedia para deixar de parecer parado.
///
/// SEGURANÇA: a prévia de nota sai do mesmo renderizador da leitura (DisableHtml + esquema de link
/// saneado). As de recorte são fragmentos MONTADOS AQUI a partir de TÍTULOS de nota, que são conteúdo
/// do usuário — por isso passam por <see cref="Escapar"/>. Autenticadas e com escopo por usuário como
/// os anexos.
/// </summary>
public static class EndpointsDePrevia
{
    /// <summary>Quantas notas o espiar de um recorte mostra. É um espiar, não a lista inteira.</summary>
    private const int TetoDaPrevia = 8;

    public static IEndpointRouteBuilder MapearPrevia(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/previa/{**caminho}", async (
            HttpContext ctx,
            string caminho,
            [FromServices] EscopoDoUsuario escopo,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] ServicoDeNotas notas,
            [FromServices] ServicoDeConhecimento conhecimento,
            CancellationToken ct) =>
        {
            // Endpoint HTTP não tem circuito para perguntar quem é — declara o escopo à mão, como o
            // endpoint de anexos (ver o comentário longo em EndpointsDeAnexos).
            escopo.Definir(await apelidos.DeAsync(ctx.User), await apelidos.VaultDeAsync(ctx.User));

            if (!CaminhoNota.TentarCriar(Uri.UnescapeDataString(caminho), out var alvo, out _) || alvo is null)
                return Results.NotFound();

            var aberta = await notas.AbrirAsync(alvo, ct);
            if (!aberta.Ok || aberta.Valor is null) return Results.NotFound();

            var renderizada = await conhecimento.RenderizarAsync(aberta.Valor.Nota.Conteudo, ct);

            // O navegador pode segurar a prévia por um minuto — o mesmo prazo do cache do previa.js.
            // "private" porque a prévia É a nota: cache compartilhado (proxy) não pode guardá-la.
            ctx.Response.Headers.CacheControl = "private, max-age=60";
            return Results.Content(renderizada.Html, "text/html; charset=utf-8");
        }).RequireAuthorization().RequireRateLimiting("previa");

        // —— O ESPIAR DE UMA ETIQUETA ——
        rotas.MapGet("/previa-etiqueta/{**valor}", async (
            HttpContext ctx, string valor,
            [FromServices] EscopoDoUsuario escopo,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] ServicoDeNotas notas,
            CancellationToken ct) =>
        {
            escopo.Definir(await apelidos.DeAsync(ctx.User), await apelidos.VaultDeAsync(ctx.User));

            if (Etiqueta.TentarCriar(Uri.UnescapeDataString(valor)) is not { } etiqueta)
                return Results.NotFound();

            // A busca já entende a hierarquia (#direito traz #direito/penal) — a mesma regra do painel.
            var acertos = await notas.BuscarAsync(
                new ConsultaDeBusca { Etiqueta = etiqueta, Limite = TetoDaPrevia + 1 }, ct);

            ctx.Response.Headers.CacheControl = "private, max-age=60";
            return Results.Content(
                Fragmento("#" + etiqueta.Valor, acertos), "text/html; charset=utf-8");
        }).RequireAuthorization().RequireRateLimiting("previa");

        // —— O ESPIAR DE UMA MATÉRIA ——
        rotas.MapGet("/previa-materia/{**valor}", async (
            HttpContext ctx, string valor,
            [FromServices] EscopoDoUsuario escopo,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] ServicoDeNotas notas,
            CancellationToken ct) =>
        {
            escopo.Definir(await apelidos.DeAsync(ctx.User), await apelidos.VaultDeAsync(ctx.User));

            var materia = Materia.De(Uri.UnescapeDataString(valor));
            if (!materia.Existe) return Results.NotFound();

            var acertos = await notas.BuscarAsync(
                new ConsultaDeBusca { Pasta = materia.Nome, Limite = TetoDaPrevia + 1 }, ct);

            ctx.Response.Headers.CacheControl = "private, max-age=60";
            return Results.Content(Fragmento(materia.Rotulo, acertos), "text/html; charset=utf-8");
        }).RequireAuthorization().RequireRateLimiting("previa");

        return rotas;
    }

    /// <summary>
    /// O fragmento do espiar: o nome do recorte e até <see cref="TetoDaPrevia"/> títulos de nota, cada um
    /// um link. Se veio mais que o teto (pedimos teto+1), diz "e mais…" em vez de mentir que é tudo.
    /// </summary>
    private static string Fragmento(string cabeca, IReadOnlyList<Acerto> acertos)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"previa-recorte-cabeca\">").Append(Escapar(cabeca)).Append("</div>");

        if (acertos.Count == 0)
            return sb.Append("<div class=\"previa-recorte-vazio\">Nenhuma nota.</div>").ToString();

        sb.Append("<ul class=\"previa-recorte-lista\">");
        foreach (var a in acertos.Take(TetoDaPrevia))
            sb.Append("<li><a href=\"notas/").Append(CaminhoNaUrl(a.Nota.Caminho))
              .Append("\">").Append(Escapar(a.Nota.Titulo)).Append("</a></li>");
        sb.Append("</ul>");

        if (acertos.Count > TetoDaPrevia)
            sb.Append("<div class=\"previa-recorte-mais\">e mais…</div>");

        return sb.ToString();
    }

    private static string CaminhoNaUrl(CaminhoNota caminho) =>
        string.Join('/', caminho.Valor.Split('/').Select(Uri.EscapeDataString));

    /// <summary>Título é conteúdo do usuário e vai para dentro de HTML montado à mão — escapa o que morde.</summary>
    private static string Escapar(string texto) => texto
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
