using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Infrastructure.Indice;

/// <summary>
/// O histórico. Adaptador de <see cref="IHistoricoDeNotas"/>.
///
/// Ao contrário do índice, ISTO NÃO É RECONSTRUÍVEL: guarda texto que não existe mais em arquivo nenhum.
/// Toda rotina de manutenção que truncar as tabelas do índice tem de deixar "revisoes" em paz.
/// </summary>
public sealed class HistoricoEmPostgres(ContextoDoIndice db, IRelogio relogio) : IHistoricoDeNotas
{
    /// <summary>
    /// Quantas revisões guardar por nota.
    ///
    /// O autosave dispara a cada poucos segundos; sem poda, uma nota editada por uma tarde geraria
    /// centenas de versões quase idênticas e o banco cresceria mais rápido que o vault. 200 cobre semanas
    /// de trabalho numa nota e mantém o custo previsível.
    /// </summary>
    private const int RevisoesPorNota = 200;

    public async Task ArquivarAsync(CaminhoNota caminho, string conteudo, string? autor, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(conteudo)) return;

        db.Revisoes.Add(new RevisaoNoIndice
        {
            Caminho = caminho.Valor,
            Conteudo = conteudo,
            Em = relogio.Agora,
            Autor = autor,
        });
        await db.SaveChangesAsync(ct);

        var excedente = await db.Revisoes.Where(r => r.Caminho == caminho.Valor)
            .OrderByDescending(r => r.Em).Skip(RevisoesPorNota).Select(r => r.Id).ToListAsync(ct);
        if (excedente.Count > 0)
            await db.Revisoes.Where(r => excedente.Contains(r.Id)).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<Revisao>> ListarAsync(CaminhoNota caminho, int limite = 50, CancellationToken ct = default)
    {
        var linhas = await db.Revisoes.AsNoTracking()
            .Where(r => r.Caminho == caminho.Valor)
            .OrderByDescending(r => r.Em).Take(limite).ToListAsync(ct);
        return linhas.Select(Projetar).ToList();
    }

    public async Task<Revisao?> ObterAsync(long id, CancellationToken ct = default)
    {
        var r = await db.Revisoes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return r is null ? null : Projetar(r);
    }

    public async Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
    {
        // Sem isto, renomear apagaria a memória da nota: o histórico continuaria atrelado a um caminho
        // que não existe mais, invisível para sempre.
        await db.Revisoes.Where(r => r.Caminho == de.Valor)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Caminho, para.Valor), ct);
    }

    private static Revisao Projetar(RevisaoNoIndice r) =>
        new(r.Id, CaminhoNota.De(r.Caminho), r.Conteudo, r.Em, r.Autor);
}
