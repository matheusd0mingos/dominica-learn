using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

public sealed record RelatorioDaReconciliacao(
    int Criadas, int Alteradas, int Removidas, int Renomeadas, int LigacoesResolvidas, TimeSpan Duracao)
{
    public int Total => Criadas + Alteradas + Removidas + Renomeadas;
    public bool EstavaEmDia => Total == 0;
}

/// <summary>
/// Põe o índice de acordo com o disco.
///
/// É o caso de uso mais importante do produto, e o que quase todo clone de Obsidian erra: eles assumem
/// que só a própria aplicação escreve no vault. Aqui a premissa é o contrário — o vault abre no Obsidian
/// Desktop, então ele MUDA POR FORA, e a aplicação precisa ser capaz de descobrir isso e se corrigir
/// sozinha, na inicialização e sempre que o vigia de arquivos avisar.
///
/// A decisão de O QUE mudou é do domínio (<see cref="Reconciliador"/>, função pura). Aqui só há
/// orquestração: buscar os dois lados, perguntar ao domínio, aplicar. É essa separação que permite
/// testar "pasta inteira movida" sem tocar em disco.
/// </summary>
public sealed class ReconciliarVault(
    IRepositorioDeNotas repositorio,
    IIndiceDoVault indice,
    IRelogio relogio,
    ILogger<ReconciliarVault> log)
{
    public async Task<RelatorioDaReconciliacao> ExecutarAsync(CancellationToken ct = default)
    {
        var inicio = relogio.Agora;

        var noDisco = new List<EstadoDaNota>();
        await foreach (var e in repositorio.VarrerAsync(ct)) noDisco.Add(e);
        var noIndice = await indice.EstadoAtualAsync(ct);

        var resultado = Reconciliador.Comparar(noDisco, noIndice);
        if (resultado.EmDia)
        {
            log.LogDebug("Vault em dia: {Notas} notas.", noDisco.Count);
            return new RelatorioDaReconciliacao(0, 0, 0, 0, 0, relogio.Agora - inicio);
        }

        // O resolvedor precisa do vault DEPOIS das mudanças — uma nota criada agora pode ser o destino de
        // um link que estava quebrado ontem. Por isso ele é montado a partir do estado do disco, não do
        // índice: reindexar com um mapa velho consertaria os links errados.
        var conhecidas = await MontarConhecidasAsync(noDisco, ct);
        var resolvedor = new ResolvedorDeWikilinks(conhecidas);

        // Renomeação primeiro: preserva histórico e favoritos antes que qualquer outra etapa mexa na nota.
        foreach (var d in resultado.Renomeadas)
        {
            await indice.RenomearAsync(d.CaminhoAnterior!, d.Caminho, ct);
            await ReindexarAsync(d.Caminho, resolvedor, ct);
        }
        foreach (var d in resultado.Criadas) await ReindexarAsync(d.Caminho, resolvedor, ct);
        foreach (var d in resultado.Alteradas) await ReindexarAsync(d.Caminho, resolvedor, ct);
        foreach (var d in resultado.Removidas) await indice.RemoverAsync(d.Caminho, ct);

        var relatorio = new RelatorioDaReconciliacao(
            resultado.Criadas.Count(), resultado.Alteradas.Count(),
            resultado.Removidas.Count(), resultado.Renomeadas.Count(),
            conhecidas.Count, relogio.Agora - inicio);

        log.LogInformation(
            "Vault reconciliado em {Ms} ms: {Criadas} criadas, {Alteradas} alteradas, {Removidas} removidas, {Renomeadas} renomeadas.",
            relatorio.Duracao.TotalMilliseconds, relatorio.Criadas, relatorio.Alteradas, relatorio.Removidas, relatorio.Renomeadas);

        return relatorio;
    }

    /// <summary>
    /// Reindexa UMA nota contra o vault que já se conhece. Usado pelo salvamento — reconciliar o vault
    /// inteiro a cada autosave seria inviável em vault grande.
    /// </summary>
    public async Task ReindexarAsync(CaminhoNota caminho, ResolvedorDeWikilinks resolvedor, CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(caminho, ct);
        // Some entre a varredura e a leitura? Acontece — o Obsidian está aberto do outro lado. Não é erro:
        // a próxima reconciliação limpa a entrada.
        if (nota is null) { log.LogDebug("Nota {Caminho} sumiu durante a indexação.", caminho); return; }

        var ligacoes = resolvedor.Resolver(nota.Caminho, nota.Analise.Ligacoes);
        await indice.IndexarAsync(nota, ligacoes, ct);
    }

    /// <summary>Monta o mapa de resolução. Só lê o conteúdo das notas cujos apelidos ainda não se conhece.</summary>
    private async Task<IReadOnlyList<NotaConhecida>> MontarConhecidasAsync(
        IReadOnlyList<EstadoDaNota> noDisco, CancellationToken ct)
    {
        var apelidosConhecidos = (await indice.NotasConhecidasAsync(ct))
            .ToDictionary(n => n.Caminho, n => n.Apelidos);

        var conhecidas = new List<NotaConhecida>(noDisco.Count);
        foreach (var e in noDisco)
        {
            if (apelidosConhecidos.TryGetValue(e.Caminho, out var apelidos))
            {
                conhecidas.Add(new NotaConhecida(e.Caminho, apelidos));
                continue;
            }
            // nota nova para o índice: só aqui vale abrir o arquivo para descobrir os apelidos dela
            var nota = await repositorio.LerAsync(e.Caminho, ct);
            conhecidas.Add(new NotaConhecida(e.Caminho, nota?.Analise.Apelidos ?? []));
        }
        return conhecidas;
    }
}
