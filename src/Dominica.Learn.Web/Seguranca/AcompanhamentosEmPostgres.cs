using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// Adaptador de <see cref="IAcompanhamentosDeEstudo"/> sobre o banco da identidade.
///
/// FICA NA CAMADA WEB, e não na Infrastructure, porque é ali que o <see cref="ApplicationDbContext"/>
/// mora — ele é do ASP.NET Identity, que é detalhe de hospedagem. O mesmo motivo pelo qual o
/// <c>ApplicationUser</c> não desceu para o domínio.
/// </summary>
public sealed class AcompanhamentosEmPostgres(IDbContextFactory<ApplicationDbContext> fabrica, IRelogio relogio)
    : IAcompanhamentosDeEstudo
{
    public async Task ConcederAsync(Acompanhamento a, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        // IDEMPOTENTE: conceder de novo o mesmo acesso é o que acontece quando alguém clica duas vezes,
        // ou reconvida por não lembrar que já tinha convidado. Um erro aqui seria ruído sobre um estado
        // que já é o desejado.
        if (await db.Acompanhamentos.AnyAsync(
                x => x.Dono == a.Dono.Valor && x.Vault == a.Vault.Valor && x.Convidado == a.Convidado.Valor, ct))
            return;

        db.Acompanhamentos.Add(new AcompanhamentoNoBanco
        {
            Dono = a.Dono.Valor,
            Vault = a.Vault.Valor,
            Convidado = a.Convidado.Valor,
            Em = relogio.Agora,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RevogarAsync(Acompanhamento a, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var apagadas = await db.Acompanhamentos
            .Where(x => x.Dono == a.Dono.Valor && x.Vault == a.Vault.Valor && x.Convidado == a.Convidado.Valor)
            .ExecuteDeleteAsync(ct);
        return apagadas > 0;
    }

    public async Task<IReadOnlyList<Acompanhamento>> QuemMeAcompanhaAsync(
        ApelidoDoUsuario dono, NomeDoVault vault, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var linhas = await db.Acompanhamentos.AsNoTracking()
            .Where(x => x.Dono == dono.Valor && x.Vault == vault.Valor)
            .OrderBy(x => x.Convidado)
            .ToListAsync(ct);
        return Traduzir(linhas);
    }

    public async Task<IReadOnlyList<Acompanhamento>> QueEuAcompanhoAsync(
        ApelidoDoUsuario convidado, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var linhas = await db.Acompanhamentos.AsNoTracking()
            .Where(x => x.Convidado == convidado.Valor)
            .OrderBy(x => x.Dono).ThenBy(x => x.Vault)
            .ToListAsync(ct);
        return Traduzir(linhas);
    }

    public async Task<bool> PodeVerAsync(
        ApelidoDoUsuario convidado, ApelidoDoUsuario dono, NomeDoVault vault, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        // OS TRÊS CAMPOS, SEMPRE. Esquecer o vault aqui daria a permissão mais perigosa possível: quem
        // foi convidado para o vault de estudo passaria a ver também o de trabalho, e ninguém veria isso
        // acontecer — a tela mostraria números plausíveis, do vault errado.
        return await db.Acompanhamentos.AnyAsync(
            x => x.Convidado == convidado.Valor && x.Dono == dono.Valor && x.Vault == vault.Valor, ct);
    }

    // Uma linha do banco pode não virar um Acompanhamento válido (um apelido que deixou de ser válido
    // depois de uma mudança de regra, por exemplo). Descartar em silêncio é o certo: a linha inválida
    // não autoriza nada, e é o `PodeVerAsync` — que consulta o banco direto — quem decide o acesso.
    private static IReadOnlyList<Acompanhamento> Traduzir(IEnumerable<AcompanhamentoNoBanco> linhas) =>
    [
        .. linhas.Select(x =>
            {
                ApelidoDoUsuario.TentarCriar(x.Dono, out var dono, out _);
                ApelidoDoUsuario.TentarCriar(x.Convidado, out var convidado, out _);
                return Acompanhamento.TentarCriar(dono, NomeDoVault.Conhecido(x.Vault), convidado);
            })
            .Where(a => a is not null)!,
    ];
}
