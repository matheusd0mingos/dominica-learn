using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;
using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Infrastructure.Registro;

/// <summary>
/// O registro de estudo, em Postgres. Adaptador de <see cref="IRegistroDeEstudo"/>.
///
/// GRAVA O NOME DA MATÉRIA COMO TEXTO, e não uma chave estrangeira para tabela de matérias — que nem
/// existe, porque matéria é a PASTA do vault. Renomear a pasta deixa o registro antigo apontando para um
/// nome que não existe mais, e isso é aceito de propósito: o registro é histórico, e histórico não muda
/// quando o presente muda. "Fiz 40 questões de Direito Tributário em março" continua verdade mesmo que a
/// pasta hoje se chame outra coisa.
/// </summary>
public sealed class RegistroEmPostgres(ContextoDoRegistro db, IUsuarioAtual usuario) : IRegistroDeEstudo
{
    private async Task<string> EuAsync(CancellationToken ct)
    {
        if (db.UsuarioAtual.Length == 0) db.UsuarioAtual = (await usuario.ApelidoAsync(ct)).Valor;
        return db.UsuarioAtual;
    }

    public async Task RegistrarQuestoesAsync(LoteDeQuestoes lote, CancellationToken ct = default)
    {
        var eu = await EuAsync(ct);
        db.Lotes.Add(new LoteNoRegistro
        {
            Usuario = eu,
            Em = lote.Em,
            Materia = lote.Materia.Nome,
            Total = lote.Total,
            Acertos = lote.Acertos,
            Segundos = (int)lote.Tempo.TotalSeconds,
            Fonte = lote.Fonte,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RegistrarSessaoAsync(SessaoDeEstudo sessao, CancellationToken ct = default)
    {
        var eu = await EuAsync(ct);
        db.Sessoes.Add(new SessaoNoRegistro
        {
            Usuario = eu,
            Inicio = sessao.Inicio,
            Materia = sessao.Materia.Nome,
            Segundos = (int)sessao.Duracao.TotalSeconds,
            Observacao = sessao.Observacao,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LoteDeQuestoes>> QuestoesAsync(DateTimeOffset desde, CancellationToken ct = default)
    {
        await EuAsync(ct);
        var linhas = await db.Lotes.AsNoTracking()
            .Where(l => l.Em >= desde).OrderByDescending(l => l.Em).ToListAsync(ct);

        var lotes = new List<LoteDeQuestoes>(linhas.Count);
        foreach (var l in linhas)
            // Linha malformada é PULADA, não explode a tela. O domínio é a autoridade sobre o que é um
            // lote válido, e um dado velho gravado antes de uma regra nova não pode derrubar o painel.
            if (LoteDeQuestoes.TentarCriar(Materia.De(l.Materia), l.Em, l.Total, l.Acertos,
                    TimeSpan.FromSeconds(l.Segundos), l.Fonte, out var lote, out _) && lote is not null)
                lotes.Add(lote);
        return lotes;
    }

    public async Task<IReadOnlyList<SessaoDeEstudo>> SessoesAsync(DateTimeOffset desde, CancellationToken ct = default)
    {
        await EuAsync(ct);
        var linhas = await db.Sessoes.AsNoTracking()
            .Where(s => s.Inicio >= desde).OrderByDescending(s => s.Inicio).ToListAsync(ct);

        var sessoes = new List<SessaoDeEstudo>(linhas.Count);
        foreach (var s in linhas)
            if (SessaoDeEstudo.TentarCriar(Materia.De(s.Materia), s.Inicio,
                    TimeSpan.FromSeconds(s.Segundos), s.Observacao, out var sessao) && sessao is not null)
                sessoes.Add(sessao);
        return sessoes;
    }

    public async Task<IReadOnlyList<LancamentoRegistrado>> UltimosAsync(
        int limite, CancellationToken ct = default)
    {
        await EuAsync(ct);

        // DUAS CONSULTAS E UMA JUNÇÃO NA MEMÓRIA, e não um UNION no banco. São duas tabelas com colunas
        // diferentes; uni-las em SQL exigiria projetar as duas para um formato comum e escrever o texto da
        // descrição dentro da consulta — que é onde ele nunca mais seria encontrado por quem for mudá-lo.
        // O `limite` é pequeno (uma tela), então o custo é uma lista de dezenas de itens.
        var lotes = await db.Lotes.AsNoTracking()
            .OrderByDescending(l => l.Em).Take(limite).ToListAsync(ct);
        var sessoes = await db.Sessoes.AsNoTracking()
            .OrderByDescending(s => s.Inicio).Take(limite).ToListAsync(ct);

        return lotes
            .Select(l => new LancamentoRegistrado(
                TipoDeLancamento.Questoes, l.Id, l.Em, Materia.De(l.Materia), DescreverLote(l)))
            .Concat(sessoes.Select(s => new LancamentoRegistrado(
                TipoDeLancamento.Tempo, s.Id, s.Inicio, Materia.De(s.Materia), DescreverSessao(s))))
            .OrderByDescending(x => x.Em)
            .Take(limite)
            .ToList();
    }

    public async Task<bool> ApagarAsync(TipoDeLancamento tipo, int id, CancellationToken ct = default)
    {
        await EuAsync(ct);

        // O filtro global por usuário AINDA VALE aqui, e é ele que faz o `id` de outra pessoa
        // simplesmente não ser encontrado. Sem ele, um identificador chutado apagaria o registro alheio —
        // que é o tipo de furo que não aparece em teste nenhum feito com um usuário só.
        if (tipo == TipoDeLancamento.Questoes)
        {
            var l = await db.Lotes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null) return false;
            db.Lotes.Remove(l);
        }
        else
        {
            var s = await db.Sessoes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return false;
            db.Sessoes.Remove(s);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string DescreverLote(LoteNoRegistro l) =>
        l.Total > 0
            ? $"{l.Total} questões · {(l.Acertos * 100.0 / l.Total):0.#}% de acerto"
            : $"{l.Total} questões";

    private static string DescreverSessao(SessaoNoRegistro s)
    {
        var minutos = s.Segundos / 60;
        return minutos < 60 ? $"{minutos} min" : $"{minutos / 60}h{minutos % 60:00}";
    }
}
