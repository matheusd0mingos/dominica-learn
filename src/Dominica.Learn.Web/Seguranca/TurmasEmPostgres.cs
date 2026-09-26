using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>Adaptador de <see cref="ITurmas"/> sobre o banco da identidade.</summary>
public sealed class TurmasEmPostgres(IDbContextFactory<ApplicationDbContext> fabrica, IRelogio relogio) : ITurmas
{
    public async Task CriarAsync(Turma turma, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        db.Turmas.Add(new TurmaNoBanco
        {
            Codigo = turma.Codigo.Valor,
            Dono = turma.Dono.Valor,
            Nome = turma.Nome,
            CriadaEm = relogio.Agora,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<Turma?> PorCodigoAsync(CodigoDeTurma codigo, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var linha = await db.Turmas.AsNoTracking().FirstOrDefaultAsync(t => t.Codigo == codigo.Valor, ct);
        return linha is null ? null : Traduzir(linha);
    }

    public async Task<IReadOnlyList<Turma>> DeQuemAsync(ApelidoDoUsuario dono, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var linhas = await db.Turmas.AsNoTracking()
            .Where(t => t.Dono == dono.Valor).OrderBy(t => t.Nome).ToListAsync(ct);
        return [.. linhas.Select(Traduzir).Where(t => t is not null)!];
    }

    public async Task<IReadOnlyList<Turma>> EmQueEstouAsync(ApelidoDoUsuario aluno, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        // JUNÇÃO E NÃO DUAS IDAS: a lista do aluno é pequena, mas duas consultas aqui significariam
        // montar a lista de códigos em memória e perguntar de novo — e a segunda pergunta pode pegar um
        // estado diferente da primeira, mostrando uma turma que acabou de ser apagada.
        var linhas = await (from m in db.Matriculas.AsNoTracking()
                            join t in db.Turmas.AsNoTracking() on m.Turma equals t.Codigo
                            where m.Aluno == aluno.Valor
                            orderby t.Nome
                            select t).ToListAsync(ct);
        return [.. linhas.Select(Traduzir).Where(t => t is not null)!];
    }

    public async Task MatricularAsync(
        CodigoDeTurma codigo, ApelidoDoUsuario aluno, NomeDoVault vault, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var jaEsta = await db.Matriculas
            .FirstOrDefaultAsync(m => m.Turma == codigo.Valor && m.Aluno == aluno.Valor, ct);

        if (jaEsta is not null)
        {
            // ENTRAR DE NOVO ATUALIZA O VAULT. Quem trocou de vault e reentrou na turma está dizendo
            // "acompanhe ESTE agora" — manter o antigo faria o professor abrir um painel que o aluno
            // não quis mais mostrar, e ainda por cima achando que era o certo.
            jaEsta.Vault = vault.Valor;
            await db.SaveChangesAsync(ct);
            return;
        }

        db.Matriculas.Add(new MatriculaNoBanco
        {
            Turma = codigo.Valor,
            Aluno = aluno.Valor,
            Vault = vault.Valor,
            EntrouEm = relogio.Agora,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DesmatricularAsync(CodigoDeTurma codigo, ApelidoDoUsuario aluno, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        return await db.Matriculas
            .Where(m => m.Turma == codigo.Valor && m.Aluno == aluno.Valor)
            .ExecuteDeleteAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<AlunoDaTurma>> AlunosAsync(CodigoDeTurma codigo, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var turma = await db.Turmas.AsNoTracking().FirstOrDefaultAsync(t => t.Codigo == codigo.Valor, ct);
        if (turma is null) return [];

        // O `Acesso` SAI DA TABELA DE ACOMPANHAMENTO, e não da matrícula: quem manda no acesso é o dono
        // do painel, sempre. Um aluno que revogou continua matriculado — e a lista do professor precisa
        // dizer isso, em vez de mostrar um nome que vai dar "sem acesso" no clique.
        var linhas = await (from m in db.Matriculas.AsNoTracking()
                            where m.Turma == codigo.Valor
                            join a in db.Acompanhamentos.AsNoTracking()
                                on new { Dono = m.Aluno, m.Vault, Convidado = turma.Dono }
                                equals new { a.Dono, a.Vault, a.Convidado } into acesso
                            from a in acesso.DefaultIfEmpty()
                            orderby m.Aluno
                            select new { m.Aluno, m.Vault, m.EntrouEm, Tem = a != null }).ToListAsync(ct);

        return
        [
            .. linhas.Select(x =>
                {
                    ApelidoDoUsuario.TentarCriar(x.Aluno, out var aluno, out _);
                    var vault = NomeDoVault.Conhecido(x.Vault);
                    return aluno is null || vault is null ? null : new AlunoDaTurma(aluno, vault, x.EntrouEm, x.Tem);
                })
                .Where(x => x is not null)!,
        ];
    }

    public async Task<bool> ApagarAsync(CodigoDeTurma codigo, ApelidoDoUsuario dono, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        // O DONO ENTRA NO WHERE, e não numa checagem antes: assim não existe janela entre conferir e
        // apagar, e uma chamada com o código de outra pessoa simplesmente não apaga nada.
        var apagadas = await db.Turmas
            .Where(t => t.Codigo == codigo.Valor && t.Dono == dono.Valor)
            .ExecuteDeleteAsync(ct);
        if (apagadas == 0) return false;

        await db.Matriculas.Where(m => m.Turma == codigo.Valor).ExecuteDeleteAsync(ct);
        return true;
    }

    private static Turma? Traduzir(TurmaNoBanco linha)
    {
        ApelidoDoUsuario.TentarCriar(linha.Dono, out var dono, out _);
        return CodigoDeTurma.TentarCriar(linha.Codigo, out var codigo, out _)
            ? Turma.TentarCriar(codigo, dono, linha.Nome)
            : null;
    }
}
