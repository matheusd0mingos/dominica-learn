using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Domain.Anexos;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace Dominica.Learn.Web.Anexos;

/// <summary>
/// Serve os anexos do vault por HTTP.
///
/// POR QUE UM ENDPOINT E NÃO <c>UseStaticFiles</c> APONTADO PARA O VAULT: static files serve a pasta
/// INTEIRA, sem autenticação e sem lista de permissão. Apontá-lo para o vault publicaria todas as notas
/// .md do usuário na internet — e a pasta .obsidian junto. Aqui, cada byte que sai passa por três
/// verificações: o usuário está autenticado, o caminho não sai da raiz, e a extensão está na lista.
///
/// O <c>Content-Disposition: inline</c> com nome de arquivo, mais o <c>nosniff</c> global, são o que
/// impede que um arquivo com nome enganoso seja interpretado como outra coisa pelo navegador.
/// </summary>
public static class EndpointsDeAnexos
{
    public static IEndpointRouteBuilder MapearAnexos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/anexos").RequireAuthorization();

        grupo.MapGet("/{**caminho}", async (
            HttpContext ctx,
            string caminho,
            [FromServices] EscopoDoUsuario escopo,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] ServicoDeAnexos servico,
            CancellationToken ct) =>
        {
            // DE QUEM É E DE QUAL VAULT, declarado aqui. Isto é um endpoint HTTP: não há circuito do
            // Blazor para perguntar, e o adaptador padrão pergunta ao provedor de estado do circuito.
            // Sem estas duas linhas o anexo não é encontrado — a nota mostra a moldura de imagem
            // quebrada e nada mais, que foi exatamente o que aconteceu com o primeiro desenho.
            //
            // É o mesmo mecanismo que o download do pacote já usava (ver RotaDoPacote); eu o apliquei
            // lá e não aqui, e o vault tornou o esquecimento visível.
            escopo.Definir(await apelidos.DeAsync(ctx.User), await apelidos.VaultDeAsync(ctx.User));

            if (!CaminhoDeAnexo.TentarCriar(Uri.UnescapeDataString(caminho), out var alvo, out _) || alvo is null)
                return Results.NotFound();

            // Lista de PERMISSÃO na saída também, e não só na entrada: o vault pode ter recebido qualquer
            // arquivo por sincronização do Obsidian, sem passar pelo upload desta aplicação.
            if (!TiposDeAnexo.EhPermitida(alvo.Extensao)) return Results.NotFound();

            var fluxo = await servico.AbrirAsync(alvo, ct);
            if (fluxo is null) return Results.NotFound();

            return Results.File(fluxo, TiposDeAnexo.TipoDe(alvo.Extensao),
                fileDownloadName: null,            // inline: a imagem aparece na nota em vez de baixar
                enableRangeProcessing: true);      // áudio e vídeo dependem de Range para poder buscar
        });

        return rotas;
    }
}
