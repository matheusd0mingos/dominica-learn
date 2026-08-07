using Dominica.Learn.Application.Portas;
using Dominica.Learn.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Dominica.Learn.Web.Vault;

/// <summary>
/// O download do vault inteiro.
///
/// POR QUE UM ENDPOINT E NÃO UM BOTÃO NO BLAZOR: baixar um vault de anos por dentro do circuito
/// significaria carregar o zip inteiro na memória do servidor, mandá-lo pelo WebSocket em pedaços de
/// base64 e remontá-lo no navegador. Um GET comum entrega o arquivo direto, com barra de progresso do
/// navegador e retomada — e sem custar um byte de RAM a mais do que o buffer da rede.
///
/// O PREÇO DISSO é que aqui não existe circuito, e é do circuito que o resto do sistema costuma saber de
/// quem é o vault. Por isso a primeira coisa que este endpoint faz é declarar o dono no escopo, a partir
/// do <c>HttpContext</c> que ele recebeu — o mesmo mecanismo que o vigia do vault já usa para trabalhar
/// fora de qualquer sessão. Sem essa linha, o empacotador perguntaria ao provedor de estado do Blazor e
/// receberia uma exceção, porque não há componente nenhum sendo renderizado.
/// </summary>
public static class RotaDoPacote
{
    public static IEndpointRouteBuilder MapearPacoteDoVault(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/vault.zip", async (
            HttpContext ctx,
            [FromServices] EscopoDoUsuario escopo,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] IEmpacotadorDoVault empacotador,
            CancellationToken ct) =>
        {
            escopo.Definir(await apelidos.DeAsync(ctx.User));

            // O nome do arquivo leva a data: quem baixa duas vezes na mesma pasta não sobrescreve o
            // backup anterior sem perceber.
            var nome = $"dominica-learn-{DateTime.Now:yyyy-MM-dd}.zip";

            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{nome}\"";

            // POR QUE NÃO SE ESCREVE DIRETO NO CORPO DA RESPOSTA, embora essa fosse a versão óbvia:
            // ao ser fechado, o ZipArchive grava o índice central do .zip com uma escrita SÍNCRONA, e o
            // Kestrel proíbe escrita síncrona no socket. O resultado do jeito óbvio é o pior possível —
            // a resposta já saiu com 200 e o cabeçalho certo, e só o final do arquivo não vai: um .zip
            // truncado que o navegador salva sem reclamar e que só falha na hora de abrir, semanas
            // depois, quando a pessoa precisa do backup. Foi exatamente o que aconteceu aqui.
            //
            // A saída NÃO é liberar escrita síncrona: isso prenderia uma thread do servidor pelo tempo
            // que o navegador do outro lado levasse para receber, e um punhado de downloads lentos
            // seguraria o servidor inteiro. Este fluxo absorve as escritas síncronas em memória até 4 MB,
            // passa para um arquivo temporário além disso, e só então despeja no socket de forma
            // assíncrona. A memória fica limitada, e o vault pode ser maior que o servidor.
            await using var acumulado = new FileBufferingWriteStream(memoryThreshold: 4 * 1024 * 1024);
            await empacotador.ExportarAsync(acumulado, ct);
            await acumulado.DrainBufferAsync(ctx.Response.Body, ct);
        })
        .RequireAuthorization();   // o vault é de UMA pessoa; a rota sem isto entregaria o de qualquer uma

        return rotas;
    }
}
