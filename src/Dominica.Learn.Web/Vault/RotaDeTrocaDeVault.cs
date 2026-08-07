using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Infrastructure.Vault;
using Dominica.Learn.Web.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Web.Vault;

/// <summary>
/// Trocar de vault, e criar vault.
///
/// POR QUE ENDPOINT HTTP E NÃO UM COMPONENTE: o alternador vive na BARRA DO TOPO, e a barra é estática
/// de propósito — ela envolve também as telas do Identity, que são formulários HTML comuns (ver
/// MainLayout). Um componente interativo ali não teria circuito em metade das páginas.
///
/// E há uma segunda razão, mais forte: trocar de vault tem de RECARREGAR A PÁGINA. Tudo que está na
/// tela — lista de notas, grafo, fila de revisão, editor aberto — foi montado a partir do vault
/// anterior. Um clique que trocasse a preferência sem recarregar deixaria a tela mostrando o vault
/// velho com o novo já valendo por baixo, e a primeira gravação iria para o lugar errado. Um POST
/// seguido de redirecionamento é exatamente o que se quer aqui, e é o que o navegador faz sozinho.
///
/// É POST, E NÃO GET, porque isto muda estado gravado. Um GET que muda estado é o que faz um
/// pré-carregador de link do navegador trocar o vault de alguém sem ninguém ter clicado.
/// </summary>
public static class RotaDeTrocaDeVault
{
    public const string Caminho = "/vault/trocar";

    public static IEndpointRouteBuilder MapearTrocaDeVault(this IEndpointRouteBuilder rotas)
    {
        rotas.MapPost(Caminho, async (
            HttpContext ctx,
            [FromForm] string vault,
            [FromForm] string? voltarPara,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] IOptions<OpcoesDoVault> opcoes,
            [FromServices] ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger(nameof(RotaDeTrocaDeVault));
            var apelido = await apelidos.DeAsync(ctx.User);

            if (!NomeDoVault.TentarCriar(vault, out var escolhido, out var porQue) || escolhido is null)
            {
                log.LogWarning("Troca de vault recusada para {Apelido}: {Motivo}", apelido, porQue);
                return Results.Redirect(Destino(ctx, voltarPara));
            }

            // O VAULT PRECISA EXISTIR NO DISCO — e a lista sai do disco, não de uma tabela. Sem esta
            // conferência, um nome digitado na mão criaria um vault vazio pelo simples fato de a raiz ser
            // criada sob demanda na primeira gravação: a pessoa se veria num vault que ela não fez, sem
            // nota nenhuma, achando que perdeu tudo.
            var existentes = RaizDoVaultDoUsuario.VaultsDe(opcoes.Value.Raiz, apelido);
            if (!existentes.Contains(escolhido))
            {
                log.LogWarning("Troca recusada: {Apelido} não tem o vault \"{Vault}\".", apelido, escolhido);
                return Results.Redirect(Destino(ctx, voltarPara));
            }

            // GRAVA E CONFERE. A versão anterior chamava e seguia, e quando a gravação não acontecia —
            // e ela não acontecia nunca, ver ApelidoDeQuemEntrou.DefinirVaultAsync — o redirecionamento
            // saía igual e a tela voltava com o vault antigo, como se o clique tivesse sido ignorado.
            if (!await apelidos.DefinirVaultAsync(ctx.User, escolhido))
                log.LogError("Não consegui gravar o vault {Vault} para {Apelido}.", escolhido, apelido);
            else
                log.LogInformation("{Apelido} agora está no vault {Vault}.", apelido, escolhido);

            return Results.Redirect(Destino(ctx, voltarPara));
        })
        .RequireAuthorization()
        .DisableAntiforgery();   // o formulário na barra estática já leva o token; ver NavMenu

        // —— CRIAR VAULT ————————————————————————————————————————————————————————————————
        //
        // CRIAR UM VAULT É CRIAR UMA PASTA, e nada mais: não há tabela de vaults, do mesmo jeito que não
        // há tabela de matérias. Quem preferir fazer pelo terminal ou pelo Obsidian faz, e o app
        // enxerga na próxima renderização da barra.
        //
        // JÁ ENTRA NELE. Criar um vault e continuar no antigo obrigaria a pessoa a um segundo gesto para
        // ver o que ela acabou de pedir — e o segundo gesto está num seletor que só apareceu agora.
        rotas.MapPost("/vault/criar", async (
            HttpContext ctx,
            [FromForm] string nome,
            [FromServices] ApelidoDeQuemEntrou apelidos,
            [FromServices] IOptions<OpcoesDoVault> opcoes,
            [FromServices] ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger(nameof(RotaDeTrocaDeVault));
            var apelido = await apelidos.DeAsync(ctx.User);

            if (!NomeDoVault.TentarCriar(nome, out var novo, out var porQue) || novo is null)
            {
                log.LogWarning("Criação de vault recusada para {Apelido}: {Motivo}", apelido, porQue);
                return Results.Redirect(Absoluto(ctx, "backup"));
            }

            // Criar por cima de um vault que já existe não estraga nada — a pasta simplesmente já está
            // lá —, mas trocar para ele em seguida é o que a pessoa queria de qualquer jeito.
            RaizDoVaultDoUsuario.Criar(opcoes.Value.Raiz, apelido, novo);
            if (!await apelidos.DefinirVaultAsync(ctx.User, novo))
                log.LogError("Criei o vault {Vault} de {Apelido}, mas não consegui entrar nele.", novo, apelido);
            else
                log.LogInformation("{Apelido} criou o vault {Vault} e entrou nele.", apelido, novo);

            return Results.Redirect(Absoluto(ctx, "painel"));
        })
        .RequireAuthorization()
        .DisableAntiforgery();

        return rotas;
    }

    /// <summary>
    /// Para onde voltar depois de trocar.
    ///
    /// SÓ CAMINHO RELATIVO, e nunca o que veio no formulário sem conferir: um "voltarPara" com URL
    /// absoluta transformaria este endpoint num redirecionador aberto — o clássico jeito de mandar
    /// alguém autenticado para um site de fora achando que continua aqui dentro.
    ///
    /// O PADRÃO É O PAINEL, e não a tela em que a pessoa estava, quando não há para onde voltar: é a
    /// tela que faz sentido em qualquer vault.
    /// </summary>
    private static string Destino(HttpContext ctx, string? voltarPara) =>
        Absoluto(ctx, !string.IsNullOrWhiteSpace(voltarPara)
            && !voltarPara.StartsWith("//", StringComparison.Ordinal)
            && !voltarPara.StartsWith('/')
            && Uri.TryCreate(voltarPara, UriKind.Relative, out _)
                ? voltarPara
                : "painel");

    /// <summary>
    /// O caminho ABSOLUTO, com o PathBase na frente. É isto ou um 404.
    ///
    /// Um Location relativo — "painel" — o navegador resolve contra o DIRETÓRIO da requisição, e a
    /// requisição é /private/dominica-learn/vault/criar. O destino vira
    /// /private/dominica-learn/vault/painel, que não existe. Em desenvolvimento, servido na raiz, o
    /// erro some: /vault/painel também não existe, mas ninguém repara porque o teste segue navegando
    /// para outro lugar. Foi assim que ele passou pelos meus testes e chegou até você.
    ///
    /// PathBase é vazio quando o app é servido na raiz do domínio (o caso do subdomínio), e aí o
    /// resultado é "/painel" — que é o certo nos dois modos. Ver OpcoesDeHospedagem.
    /// </summary>
    private static string Absoluto(HttpContext ctx, string caminhoRelativo) =>
        $"{ctx.Request.PathBase}/{caminhoRelativo.TrimStart('/')}";
}
