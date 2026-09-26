using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace Dominica.Learn.Web.Vault;

/// <summary>
/// O download de UMA nota, como .md.
///
/// POR QUE EXISTE, tendo o /vault.zip ao lado: são gestos diferentes. O zip é backup — leva anos de
/// estudo e serve para guardar. Aqui a pessoa quer ESTA nota: mandar a súmula de ontem para um colega,
/// abrir num editor, anexar num e-mail. Pedir que ela baixe o vault inteiro e ache o arquivo dentro é
/// pedir que ela desista.
///
/// ENDPOINT E NÃO BOTÃO NO CIRCUITO, pela mesma razão do vault.zip: download é do navegador. Mandar o
/// arquivo pelo WebSocket em base64 para o JavaScript remontar num blob funciona e é pior em tudo — usa
/// memória do servidor, não tem barra de progresso e quebra no arquivo grande.
///
/// E VALE O MESMO PREÇO: fora do circuito ninguém sabe de quem é o vault, então a primeira coisa é
/// declarar o dono no escopo a partir do <c>HttpContext</c>. Sem isso o repositório perguntaria ao
/// provedor de estado do Blazor e receberia uma exceção, porque não há componente sendo renderizado.
///
/// O CAMINHO VEM NA QUERY, e passa pelo <see cref="CaminhoNota"/> antes de tocar o disco — é ele que
/// recusa "../" e caminho absoluto. Uma rota de download que concatena texto de query num caminho de
/// arquivo é a forma clássica de entregar /etc/passwd; aqui quem valida é o mesmo tipo que valida o
/// caminho em todo o resto do sistema, e não uma checagem escrita à parte só para esta rota.
/// </summary>
public static class RotaDaNota
{
    public static IEndpointRouteBuilder MapearDownloadDaNota(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/nota.md", async (
            [FromQuery] string? caminho,
            HttpContext ctx,
            [FromServices] EscopoDoUsuario escopo,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] IRepositorioDeNotas notas,
            CancellationToken ct) =>
        {
            if (!CaminhoNota.TentarCriar(caminho, out var alvo, out var erro) || alvo is null)
                return Results.BadRequest(erro ?? "caminho inválido.");

            escopo.Definir(await apelidos.DeAsync(ctx.User), await apelidos.VaultDeAsync(ctx.User));

            var nota = await notas.LerAsync(alvo, ct);
            // 404 e não 403: a nota é procurada DENTRO do vault de quem pediu, então "não existe" e "não é
            // sua" são a mesma resposta — e distingui-las contaria, para quem chuta caminhos, quais notas
            // existem no vault dos outros.
            if (nota is null) return Results.NotFound();

            // O NOME DO ARQUIVO É O DA NOTA, não o caminho inteiro: "Aula02_contabilidade.md" é o que a
            // pessoa espera ver na pasta de downloads — não "Contabilidade Geral_Aula02_contabilidade.md".
            var nome = alvo.Valor.Split('/')[^1];

            // UTF-8 EXPLÍCITO nos bytes e no content-type: sem isso o navegador adivinha a codificação, e
            // uma nota com acento chega com "contábil" quebrado — num arquivo que a pessoa vai guardar.
            // `fileDownloadName` é o que faz virar download em vez de abrir como texto na aba.
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(nota.Conteudo),
                "text/markdown; charset=utf-8",
                fileDownloadName: nome);
        })
        .RequireAuthorization();   // o vault é de UMA pessoa

        return rotas;
    }
}
