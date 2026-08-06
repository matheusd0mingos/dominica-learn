using Dominica.Learn.Application.CasosDeUso;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Infrastructure.Vault;

/// <summary>
/// Vigia o vault no disco e reconcilia o índice quando alguém mexe nos arquivos por fora.
///
/// É o que transforma "abre no Obsidian" de promessa em comportamento: você edita uma nota no Obsidian
/// Desktop e ela aparece atualizada no Learn sem precisar clicar em nada.
///
/// DUAS DECISÕES QUE PARECEM DETALHE E NÃO SÃO:
///
/// 1. SILÊNCIO ANTES DE AGIR. Salvar um arquivo gera três ou quatro eventos; um `git checkout` gera
///    milhares em segundos. Reconciliar a cada evento derrubaria a máquina. O vigia espera o disco ficar
///    quieto por <see cref="OpcoesDoVault.EsperaDoVigiaMs"/> e só então reconcilia UMA vez.
///
/// 2. RECONCILIAÇÃO COMPLETA, não incremental por evento. O FileSystemWatcher perde eventos sob carga —
///    é limitação conhecida dele, não defeito de uso — e um índice construído a partir de uma sequência
///    de eventos com buracos fica sutilmente errado, sem ninguém perceber. Comparar disco e índice
///    inteiros é a operação que se autocorrige: erre um evento e a próxima passada conserta.
/// </summary>
public sealed class VigiaDoVault : BackgroundService
{
    private readonly IServiceScopeFactory _escopos;
    private readonly OpcoesDoVault _opcoes;
    private readonly ILogger<VigiaDoVault> _log;
    private readonly SemaphoreSlim _acordar = new(0, 1);
    private FileSystemWatcher? _vigia;

    public VigiaDoVault(IServiceScopeFactory escopos, IOptions<OpcoesDoVault> opcoes, ILogger<VigiaDoVault> log)
    {
        _escopos = escopos;
        _opcoes = opcoes.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var raiz = Path.GetFullPath(_opcoes.Raiz);
        Directory.CreateDirectory(raiz);

        if (_opcoes.ReconciliarAoIniciar) await ReconciliarAsync(ct);

        try
        {
            _vigia = new FileSystemWatcher(raiz, "*" + Domain.Vault.CaminhoNota.Extensao)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true,
            };
            _vigia.Changed += (_, _) => Cutucar();
            _vigia.Created += (_, _) => Cutucar();
            _vigia.Deleted += (_, _) => Cutucar();
            _vigia.Renamed += (_, _) => Cutucar();
            // Estouro do buffer é justamente o caso em que eventos se perderam. Como a reconciliação é
            // completa, a resposta certa é a mesma de sempre: reconciliar. Por isso não há tratamento
            // especial — só o log, para que apareça na observabilidade se virar rotina.
            _vigia.Error += (_, e) =>
            {
                _log.LogWarning(e.GetException(), "O vigia do vault perdeu eventos; reconciliando por completo.");
                Cutucar();
            };
        }
        catch (Exception e)
        {
            // Alguns sistemas de arquivos de rede e contêineres não suportam notificação. O produto
            // continua funcionando — só não percebe a edição externa sozinho. Falhar a inicialização
            // inteira por causa disso seria trocar uma limitação por uma indisponibilidade.
            _log.LogWarning(e, "Sem vigia de arquivos neste ambiente: a edição externa só será vista ao reiniciar ou recarregar.");
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _acordar.WaitAsync(ct);
                // espera o disco ficar quieto — se chegar evento novo, o ciclo reinicia
                await Task.Delay(_opcoes.EsperaDoVigiaMs, ct);
                await ReconciliarAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                // Uma reconciliação que falha não pode derrubar o vigia: sem ele, o produto perde a
                // sincronia com o disco em silêncio, que é o pior estado possível.
                _log.LogError(e, "Falha ao reconciliar o vault; o vigia continua.");
            }
        }
    }

    private void Cutucar()
    {
        // Se já há um ciclo pendente, não enfileira outro: o objetivo é UMA reconciliação depois do
        // silêncio, não uma por evento.
        try { _acordar.Release(); } catch (SemaphoreFullException) { }
    }

    private async Task ReconciliarAsync(CancellationToken ct)
    {
        // Escopo próprio: o DbContext é scoped e este serviço é singleton. Resolver o contexto direto do
        // provedor raiz é o clássico "captured dependency" — funciona no teste e vaza conexão em produção.
        using var escopo = _escopos.CreateScope();
        var reconciliacao = escopo.ServiceProvider.GetRequiredService<ReconciliarVault>();
        await reconciliacao.ExecutarAsync(ct);
    }

    public override void Dispose()
    {
        _vigia?.Dispose();
        _acordar.Dispose();
        base.Dispose();
    }
}
