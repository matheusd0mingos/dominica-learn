using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace Dominica.Learn.Web.Notas;

/// <summary>
/// A prévia de uma nota — o fragmento de HTML que o popover de hover mostra.
///
/// É UM ENDPOINT, e não interop do circuito, porque o hover é do navegador: o mouse paira sobre um link
/// e a resposta precisa vir sem acordar o Blazor — um round-trip de circuito por hover deixaria o
/// ponteiro na frente do popover. O fetch é feito por js/previa.js, com URL RELATIVA (respeita o
/// &lt;base href&gt; quando o app é servido sob sub-caminho).
///
/// O QUE SAI DAQUI JÁ É SEGURO: é o mesmo renderizador da leitura (DisableHtml — HTML bruto escapado na
/// origem), então o innerHTML do popover recebe o que a tela de leitura já recebia. Autenticado como os
/// anexos, e pela mesma razão: a prévia É a nota.
/// </summary>
public static class EndpointsDePrevia
{
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
            return Results.Content(renderizada.Html, "text/html; charset=utf-8");
        }).RequireAuthorization();

        return rotas;
    }
}
