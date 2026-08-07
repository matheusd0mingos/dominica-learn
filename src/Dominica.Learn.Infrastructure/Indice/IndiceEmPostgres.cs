using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;
using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Infrastructure.Indice;

/// <summary>
/// O índice, em Postgres. Adaptador de <see cref="IIndiceDoVault"/> — o domínio não sabe que ele existe.
/// </summary>
public sealed class IndiceEmPostgres(IDbContextFactory<ContextoDoIndice> fabrica, IUsuarioAtual usuario) : IIndiceDoVault
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

    public async Task<IReadOnlyList<EstadoDaNota>> EstadoAtualAsync(CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var linhas = await db.Notas.AsNoTracking()
            .Select(n => new { n.Caminho, n.ModificadoEm, n.Impressao })
            .ToListAsync(ct);

        var estados = new List<EstadoDaNota>(linhas.Count);
        foreach (var l in linhas)
            if (CaminhoNota.TentarCriar(l.Caminho, out var c, out _) && c is not null)
                estados.Add(new EstadoDaNota(c, l.ModificadoEm, ImpressaoDigital.Reidratar(l.Impressao)));
        return estados;
    }

    public async Task IndexarAsync(Nota nota, IReadOnlyList<LigacaoResolvida> ligacoes, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var eu = db.UsuarioAtual;
        var vault = db.VaultAtual;
        var entidade = await db.Notas
            .Include(n => n.Ligacoes).Include(n => n.Etiquetas)
            .FirstOrDefaultAsync(n => n.Caminho == nota.Caminho.Valor, ct);

        if (entidade is null)
        {
            entidade = new NotaNoIndice { Caminho = nota.Caminho.Valor, Usuario = eu, Vault = vault };
            db.Notas.Add(entidade);
        }
        else
        {
            // Substituição total, e não diferença incremental: a análise é função pura do conteúdo, então
            // recalcular tudo é mais barato e MUITO mais seguro que casar item a item. Diferença incremental
            // é onde nasce índice que discorda do arquivo.
            db.Ligacoes.RemoveRange(entidade.Ligacoes);
            db.Etiquetas.RemoveRange(entidade.Etiquetas);
        }

        var a = nota.Analise;
        entidade.Titulo = nota.Titulo;
        entidade.Resumo = a.Resumo;
        entidade.Conteudo = nota.Conteudo;
        entidade.Impressao = nota.Impressao.Valor;
        entidade.ModificadoEm = nota.ModificadoEm;
        entidade.Palavras = a.Palavras;
        entidade.Apelidos = string.Join('\n', a.Apelidos);

        entidade.Ligacoes = ligacoes
            .Where(l => !l.EhInterna)
            .Select(l => new LigacaoNoIndice
            {
                Usuario = eu,
            Vault = vault,
                Alvo = l.Alvo,
                Destino = l.Destino?.Valor,
                Secao = l.Secao,
                Rotulo = l.Rotulo,
                Forma = (int)l.Forma,
                Posicao = l.Posicao,
            }).ToList();

        // A etiqueta é gravada COM OS ANCESTRAIS. É o que faz "tudo de #direito" trazer também o que está
        // marcado só como #direito/penal — sem isso, a hierarquia seria decorativa e a consulta viraria um
        // LIKE 'direito%' que casaria "#direitos-humanos" por acidente.
        // "Propria" marca a etiqueta como a pessoa a ESCREVEU, separando-a dos ancestrais criados aqui.
        // Ver EtiquetaNoIndice.Propria: sem essa marca, o painel não consegue dizer quantas notas pararam
        // na disciplina sem chegar ao tópico.
        var escritas = a.Etiquetas.Select(e => e.Valor).ToHashSet(StringComparer.OrdinalIgnoreCase);

        entidade.Etiquetas = a.Etiquetas
            .SelectMany(e => e.ComAncestrais())
            .Select(e => e.Valor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(v => new EtiquetaNoIndice { Usuario = eu,
            Vault = vault, Valor = v, Propria = escritas.Contains(v) })
            .ToList();

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoverAsync(CaminhoNota caminho, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        await db.Notas.Where(n => n.Caminho == caminho.Valor).ExecuteDeleteAsync(ct);
    }

    public async Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        // Atualiza em vez de apagar+criar para preservar o Id — é dele que penduram histórico, favoritos e
        // (no futuro) estatísticas de estudo. Recriar a linha perderia tudo isso em silêncio.
        await db.Notas.Where(n => n.Caminho == de.Valor)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Caminho, para.Valor), ct);

        // as ligações que apontavam para o caminho antigo passam a apontar para o novo
        await db.Ligacoes.Where(l => l.Destino == de.Valor)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Destino, para.Valor), ct);
    }

    public async Task<NotaIndexada?> ObterAsync(CaminhoNota caminho, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var n = await db.Notas.AsNoTracking().Include(x => x.Etiquetas)
            .FirstOrDefaultAsync(x => x.Caminho == caminho.Valor, ct);
        return n is null ? null : Projetar(n);
    }

    public async Task<IReadOnlyList<NotaConhecida>> NotasConhecidasAsync(CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var linhas = await db.Notas.AsNoTracking().Select(n => new { n.Caminho, n.Apelidos }).ToListAsync(ct);
        var conhecidas = new List<NotaConhecida>(linhas.Count);
        foreach (var l in linhas)
            if (CaminhoNota.TentarCriar(l.Caminho, out var c, out _) && c is not null)
                conhecidas.Add(new NotaConhecida(c, SepararApelidos(l.Apelidos)));
        return conhecidas;
    }

    public async Task<IReadOnlyList<Acerto>> BuscarAsync(ConsultaDeBusca consulta, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var q = db.Notas.AsNoTracking().Include(n => n.Etiquetas).AsQueryable();

        if (!string.IsNullOrWhiteSpace(consulta.Pasta))
            q = q.Where(n => n.Caminho.StartsWith(consulta.Pasta + "/"));

        // IGUALDADE EXATA, e ainda assim hierárquica: quem grava é que já expandiu. Toda nota marcada com
        // "#direito/penal" tem também uma linha "direito" (ver IndexarAsync), então "#direito" traz as
        // filhas sem nenhum LIKE — que casaria "#direitos-humanos" por acidente. A hierarquia mora no que
        // se GRAVA, não no que se consulta, e é por isso que esta linha pode ser tão simples.
        if (consulta.Etiqueta is { } etiqueta)
            q = q.Where(n => n.Etiquetas.Any(e => e.Valor.ToLower() == etiqueta.Valor.ToLower()));

        var texto = consulta.Texto?.Trim();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            // ILIKE simples, de propósito. Busca em português com radicalização exige a configuração de
            // dicionário do Postgres, e ligar full-text agora sem medir seria otimizar antes de saber o
            // tamanho do vault. A porta IIndiceDoVault não muda quando isto virar tsvector — que é o
            // ponto de ter uma porta.
            var padrao = $"%{texto}%";
            q = q.Where(n => EF.Functions.ILike(n.Titulo, padrao)
                          || EF.Functions.ILike(n.Conteudo, padrao)
                          || EF.Functions.ILike(n.Caminho, padrao));
        }

        var notas = await q.OrderByDescending(n => n.ModificadoEm).Take(consulta.Limite).ToListAsync(ct);

        return notas.Select(n => new Acerto(
            Projetar(n),
            TrechoEmVolta(n.Conteudo, texto) is { Length: > 0 } t ? t : n.Resumo,
            Relevancia(n, texto))).OrderByDescending(x => x.Relevancia).ToList();
    }

    public async Task<IReadOnlyList<LigacaoResolvida>> BacklinksAsync(CaminhoNota caminho, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var linhas = await db.Ligacoes.AsNoTracking().Include(l => l.Nota)
            .Where(l => l.Destino == caminho.Valor)
            .OrderBy(l => l.Nota.Caminho).ThenBy(l => l.Posicao)
            .ToListAsync(ct);
        return linhas.Select(l => Projetar(l, l.Nota.Caminho)).Where(l => l is not null).Select(l => l!).ToList();
    }

    public async Task<IReadOnlyList<LigacaoResolvida>> LigacoesDeAsync(CaminhoNota caminho, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var linhas = await db.Ligacoes.AsNoTracking().Include(l => l.Nota)
            .Where(l => l.Nota.Caminho == caminho.Valor).OrderBy(l => l.Posicao)
            .ToListAsync(ct);
        return linhas.Select(l => Projetar(l, caminho.Valor)).Where(l => l is not null).Select(l => l!).ToList();
    }

    public async Task<IReadOnlyList<EtiquetaContada>> EtiquetasAsync(CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var linhas = await db.Etiquetas.AsNoTracking()
            .GroupBy(e => e.Valor)
            .Select(g => new { Valor = g.Key, Notas = g.Select(x => x.NotaId).Distinct().Count() })
            .ToListAsync(ct);

        return linhas
            .Select(l => new { E = Etiqueta.TentarCriar(l.Valor), l.Notas })
            .Where(x => x.E is not null)
            .Select(x => new EtiquetaContada(x.E!, x.Notas))
            .OrderBy(x => x.Etiqueta.Valor, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<IReadOnlyList<Etiqueta>>> EtiquetasPorNotaAsync(CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        // Só as ESCRITAS. Os ancestrais são recriados pela árvore, que sabe não contar a mesma nota duas
        // vezes; trazê-los daqui faria "quantas notas param neste nó" ser sempre igual ao total do galho.
        var linhas = await db.Etiquetas.AsNoTracking()
            .Where(e => e.Propria)
            .Select(e => new { e.NotaId, e.Valor })
            .ToListAsync(ct);

        return linhas
            .GroupBy(l => l.NotaId)
            .Select(g => (IReadOnlyList<Etiqueta>)g
                .Select(l => Etiqueta.TentarCriar(l.Valor))
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList())
            .ToList();
    }

    public async Task<IReadOnlyList<NotaIndexada>> RecentesAsync(int limite, CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var notas = await db.Notas.AsNoTracking().Include(n => n.Etiquetas)
            .OrderByDescending(n => n.ModificadoEm).Take(limite).ToListAsync(ct);
        return notas.Select(Projetar).ToList();
    }

    public async Task<IReadOnlyList<CaminhoNota>> TodosOsCaminhosAsync(CancellationToken ct = default)
    {
        await using var db = await AbrirAsync(ct);
        var caminhos = await db.Notas.AsNoTracking().OrderBy(n => n.Caminho).Select(n => n.Caminho).ToListAsync(ct);
        return caminhos
            .Select(c => CaminhoNota.TentarCriar(c, out var k, out _) ? k : null)
            .Where(c => c is not null).Select(c => c!).ToList();
    }

    // —— projeções ————————————————————————————————————————————————————————————————————
    private static NotaIndexada Projetar(NotaNoIndice n) => new(
        CaminhoNota.De(n.Caminho), n.Titulo, n.Resumo, n.ModificadoEm, n.Palavras,
        n.Etiquetas.Select(e => Etiqueta.TentarCriar(e.Valor)).Where(e => e is not null).Select(e => e!).ToList(),
        SepararApelidos(n.Apelidos));

    private static LigacaoResolvida? Projetar(LigacaoNoIndice l, string origem)
    {
        if (!CaminhoNota.TentarCriar(origem, out var o, out _) || o is null) return null;
        CaminhoNota? destino = null;
        if (l.Destino is not null) CaminhoNota.TentarCriar(l.Destino, out destino, out _);
        return new LigacaoResolvida
        {
            Origem = o, Alvo = l.Alvo, Destino = destino, Secao = l.Secao,
            Rotulo = l.Rotulo, Forma = (FormaDaLigacao)l.Forma, Posicao = l.Posicao,
        };
    }

    private static string[] SepararApelidos(string bruto) =>
        string.IsNullOrEmpty(bruto) ? [] : bruto.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Trecho em volta do termo, para o resultado mostrar ONDE casou e não só que casou.</summary>
    private static string TrechoEmVolta(string conteudo, string? termo)
    {
        if (string.IsNullOrWhiteSpace(termo) || string.IsNullOrEmpty(conteudo)) return string.Empty;
        var i = conteudo.IndexOf(termo, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return string.Empty;
        var inicio = Math.Max(0, i - 60);
        var fim = Math.Min(conteudo.Length, i + termo.Length + 100);
        var trecho = conteudo[inicio..fim].Replace('\n', ' ').Trim();
        return (inicio > 0 ? "…" : string.Empty) + trecho + (fim < conteudo.Length ? "…" : string.Empty);
    }

    /// <summary>
    /// Ordenação simples e explicável: casar no TÍTULO vale mais que casar no corpo, e nota mexida
    /// recentemente vale um empurrão. Não é BM25 — e admitir isso vale mais que fingir sofisticação.
    /// </summary>
    private static double Relevancia(NotaNoIndice n, string? termo)
    {
        if (string.IsNullOrWhiteSpace(termo)) return 1;
        var nota = 0.0;
        if (n.Titulo.Contains(termo, StringComparison.OrdinalIgnoreCase)) nota += 10;
        if (n.Caminho.Contains(termo, StringComparison.OrdinalIgnoreCase)) nota += 3;
        if (n.Conteudo.Contains(termo, StringComparison.OrdinalIgnoreCase)) nota += 1;
        var dias = (DateTimeOffset.UtcNow - n.ModificadoEm).TotalDays;
        return nota + (dias < 30 ? 1 : 0);
    }
}
