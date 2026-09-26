using System.Text;
using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace Dominica.Learn.Web.Vault;

/// <summary>
/// O download dos cartões no formato de importação do Anki — Arquivo → Importar, lá.
///
/// Endpoint e não circuito pela mesma razão do vault.zip: download é do navegador. O formato (e por que
/// não é .apkg) está explicado no <see cref="Dominica.Learn.Domain.Cartoes.ExportadorDeAnki"/>.
/// </summary>
public static class RotaDoAnki
{
    public static IEndpointRouteBuilder MapearExportacaoAnki(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/anki.txt", async (
            HttpContext ctx,
            [FromServices] EscopoDoUsuario escopo,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] ServicoDeCartoes cartoes,
            CancellationToken ct) =>
        {
            // Fora do circuito não há preferência a consultar — o vault vem da mesma tradução que
            // responde pelo apelido, como nos anexos e no vault.zip.
            escopo.Definir(await apelidos.DeAsync(ctx.User), await apelidos.VaultDeAsync(ctx.User));

            var vault = await apelidos.VaultDeAsync(ctx.User);
            var nome = $"dominica-learn-{vault}-anki-{DateTime.Now:yyyy-MM-dd}.txt";
            var texto = await cartoes.ExportarParaAnkiAsync(ct);

            return Results.File(Encoding.UTF8.GetBytes(texto), "text/plain; charset=utf-8", nome);
        }).RequireAuthorization();

        return rotas;
    }
}
