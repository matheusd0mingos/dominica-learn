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
public sealed class HistoricoEmPostgres(IDbContextFactory<ContextoDoIndice> fabrica, IRelogio relogio, IUsuarioAtual usuario) : IHistoricoDeNotas
{
    /// <summary>
    /// Abre um contexto SÓ PARA ESTA CHAMADA, já sabendo de quem é o vault.
    ///
    /// UM CONTEXTO POR OPERAÇÃO, E NÃO UM POR ESCOPO. No Blazor Server o escopo é o CIRCUITO — ou seja,
    /// um contexto para a aba inteira, vivo por horas. Duas operações que se sobreponham nele não
    /// devolvem dado errado: o EF lança "a second operation was started on this context instance", a
    /// exceção sobe pela renderização e O CIRCUITO MORRE. A tela continua desenhada, o autosave nunca
    /// mais chega ao servidor, e o que for digitado dali em diante some sem uma única mensagem.
    ///
    /// Isso não é hipótese: aconteceu ao trocar de nota logo depois de digitar, e custou uma sessão
    /// inteira de depuração até o log do servidor mostrar. A tela foi remendada com um cadeado; ISTO
    /// AQUI é o conserto — é a recomendação da própria Microsoft para Blazor Server, e ela tira a
    /// classe inteira do defeito em vez de tapar o buraco onde ele apareceu.
    ///
    /// O PREÇO, dito às claras: um contexto por chamada não tem cache de identidade entre chamadas, e
    /// abrir custa uma conexão do pool. Para este app — consultas curtas, poucos usuários — isso não se
    /// mede. Para o defeito que ele evita, mede-se em trabalho perdido.
    ///
    /// A REGRA DE USUÁRIO NÃO MUDOU: quem decide é o filtro global do contexto; aqui só se informa o
    /// sujeito. Esquecer esta linha devolve vazio, nunca o vault de outra pessoa.
    /// </summary>
    private async Task<ContextoDoIndice> AbrirAsync(CancellationToken ct)
    {
        var db = await fabrica.CreateDbContextAsync(ct);
        db.UsuarioAtual = (await usuario.ApelidoAsync(ct)).Valor;
        // OS DOIS LADOS, SEMPRE JUNTOS. Preencher um e esquecer o outro não dá erro: dá lista vazia
        // (porque nenhuma linha real tem vault vazio), e lista vazia parece "não tem nada" em vez de
        // "perguntei errado". Por isso as duas linhas ficam grudadas, aqui e nos outros adaptadores.
        db.VaultAtual = (await usuario.VaultAsync(ct)).Valor;
        return db;
    }

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
        await using var db = await AbrirAsync(ct);
        var eu = db.UsuarioAtual;
        var vault = db.VaultAtual;
        if (string.IsNullOrEmpty(conteudo)) return;

        db.Revisoes.Add(new RevisaoNoIndice
        {
            Usuario = eu,
            Vault = vault,
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
        await using var db = await AbrirAsync(ct);
        var linhas = await db.Revisoes.AsNoTracking()
            .Where(r => r.Caminho == caminho.Valor)
            .OrderByDescending(r => r.Em).Take(limite).ToListAsync(ct);
        return linhas.Select(Projetar).ToList();
    }

    public async Task<Revisao?> ObterAsync(long id, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var r = await db.Revisoes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return r is null ? null : Projetar(r);
    }

    public async Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        // Sem isto, renomear apagaria a memória da nota: o histórico continuaria atrelado a um caminho
        // que não existe mais, invisível para sempre.
        await db.Revisoes.Where(r => r.Caminho == de.Valor)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Caminho, para.Valor), ct);
    }

    private static Revisao Projetar(RevisaoNoIndice r) =>
        new(r.Id, CaminhoNota.De(r.Caminho), r.Conteudo, r.Em, r.Autor);
}
