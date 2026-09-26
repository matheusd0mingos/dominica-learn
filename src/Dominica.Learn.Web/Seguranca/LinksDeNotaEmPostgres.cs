using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// Adaptador de <see cref="ILinksDeNota"/> sobre o banco da identidade — irmão do
/// <see cref="AcompanhamentosEmPostgres"/>, e na camada Web pelo mesmo motivo dele.
/// </summary>
public sealed class LinksDeNotaEmPostgres(IDbContextFactory<ApplicationDbContext> fabrica) : ILinksDeNota
{
    public async Task<LinkDeNota> PublicarAsync(LinkDeNota link, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);

        // IDEMPOTENTE: já publicada, devolve o link que existe e DESCARTA o token novo. Sortear um
        // segundo deixaria dois endereços vivos para a mesma nota — e "revogar" na tela mataria só o que
        // a tela conhece, com a pessoa saindo dali convencida de que fechou o acesso. O índice único em
        // (Dono, Vault, Caminho) é a mesma regra dita pelo banco, para o caso de duas abas publicarem no
        // mesmo instante e passarem as duas por esta conferência.
        var existente = await db.LinksDeNota.AsNoTracking().FirstOrDefaultAsync(
            x => x.Dono == link.Dono.Valor && x.Vault == link.Vault.Valor && x.Caminho == link.Caminho.Valor, ct);
        if (existente is not null && Traduzir(existente) is { } jaPublicada) return jaPublicada;

        db.LinksDeNota.Add(new LinkDeNotaNoBanco
        {
            Token = link.Token.Valor,
            Dono = link.Dono.Valor,
            Vault = link.Vault.Valor,
            Caminho = link.Caminho.Valor,
            CriadoEm = link.CriadoEm,
        });
        await db.SaveChangesAsync(ct);
        return link;
    }

    public async Task<LinkDeNota?> PorTokenAsync(TokenDeLink token, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var linha = await db.LinksDeNota.AsNoTracking().FirstOrDefaultAsync(x => x.Token == token.Valor, ct);
        return linha is null ? null : Traduzir(linha);
    }

    public async Task<LinkDeNota?> DaNotaAsync(
        ApelidoDoUsuario dono, NomeDoVault vault, CaminhoNota caminho, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var linha = await db.LinksDeNota.AsNoTracking().FirstOrDefaultAsync(
            x => x.Dono == dono.Valor && x.Vault == vault.Valor && x.Caminho == caminho.Valor, ct);
        return linha is null ? null : Traduzir(linha);
    }

    public async Task<IReadOnlyList<LinkDeNota>> MeusAsync(ApelidoDoUsuario dono, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var linhas = await db.LinksDeNota.AsNoTracking()
            .Where(x => x.Dono == dono.Valor)
            .OrderByDescending(x => x.CriadoEm)
            .ToListAsync(ct);
        return [.. linhas.Select(Traduzir).Where(l => l is not null)!];
    }

    public async Task<bool> RevogarAsync(ApelidoDoUsuario dono, TokenDeLink token, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        // O DONO VAI NO WHERE, e não só o token. Sem ele, quem tivesse um link de outra pessoa poderia
        // despublicar a nota dela — o token circula, é essa a natureza dele.
        var apagadas = await db.LinksDeNota
            .Where(x => x.Token == token.Valor && x.Dono == dono.Valor)
            .ExecuteDeleteAsync(ct);
        return apagadas > 0;
    }

    // Uma linha pode não virar um link válido (um caminho que deixou de passar na validação depois de
    // uma mudança de regra). Devolver null é o certo, e o efeito é o mesmo de não existir: o visitante
    // toma 404. Uma linha que não vira link não autoriza nada.
    private static LinkDeNota? Traduzir(LinkDeNotaNoBanco x)
    {
        TokenDeLink.TentarCriar(x.Token, out var token);
        ApelidoDoUsuario.TentarCriar(x.Dono, out var dono, out _);
        CaminhoNota.TentarCriar(x.Caminho, out var caminho, out _);
        return LinkDeNota.TentarCriar(token, dono, NomeDoVault.Conhecido(x.Vault), caminho, x.CriadoEm);
    }
}
