using System.Text;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace Dominica.Learn.Web.Registro;

/// <summary>
/// O download do histórico de sessões de estudo em CSV — sem precisar baixar o vault inteiro.
///
/// O backup (vault.zip) já leva este mesmo arquivo dentro, mas quem quer olhar as horas na planilha não
/// deveria ter que baixar anos de notas para chegar nele. Endpoint e não circuito pela mesma razão do
/// vault.zip: download é do navegador. As LINHAS vêm do domínio (<see cref="RegistroEmCsv"/>) — o mesmo
/// formato do zip, escrito uma vez.
/// </summary>
public static class RotaDoRegistroEmCsv
{
    public static IEndpointRouteBuilder MapearRegistroEmCsv(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/sessoes.csv", async (
            HttpContext ctx,
            [FromServices] EscopoDoUsuario escopo,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] IRegistroDeEstudo registro,
            [FromServices] IRelogio relogio,
            CancellationToken ct) =>
        {
            // Fora do circuito não há preferência a consultar — o vault vem da mesma tradução que
            // responde pelo apelido, como no vault.zip e no anki.txt.
            escopo.Definir(await apelidos.DeAsync(ctx.User), await apelidos.VaultDeAsync(ctx.User));

            var sessoes = await registro.SessoesAsync(DateTimeOffset.MinValue, ct);
            var linhas = RegistroEmCsv.Sessoes(sessoes, relogio.Fuso);

            var vault = await apelidos.VaultDeAsync(ctx.User);
            var nome = $"dominica-learn-{vault}-sessoes-{relogio.Agora:yyyy-MM-dd}.csv";

            // BOM em UTF-8: sem ele o Excel lê "Direito tributÃ¡rio" — o mesmo detalhe do zip de backup.
            var texto = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                .GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", linhas) + "\r\n"))
                .ToArray();

            return Results.File(texto, "text/csv; charset=utf-8", nome);
        }).RequireAuthorization();   // o registro é de UMA pessoa; sem isto a rota entregaria o de qualquer uma

        return rotas;
    }
}
