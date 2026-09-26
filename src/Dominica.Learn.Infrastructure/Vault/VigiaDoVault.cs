using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
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

    /// <summary>
    /// Reconcilia o vault de CADA usuário, um por vez.
    ///
    /// A lista de usuários sai das SUBPASTAS da raiz, e não da tabela de identidade. Não é atalho: o
    /// vigia é infraestrutura do vault e não tem — nem deve ter — acesso ao banco de contas. E o critério
    /// é mais honesto assim: o que existe para reconciliar é o que está no disco. Uma conta criada e
    /// nunca usada não tem pasta, e não há nada a fazer por ela.
    /// </summary>
    private async Task ReconciliarAsync(CancellationToken ct)
    {
        // UM VAULT POR VEZ, e não um usuário por vez. Cada vault tem índice próprio: reconciliar "o
        // usuário" sem dizer qual vault compararia o disco de um com o índice do outro, e o
        // reconciliador concluiria — corretamente, pelo que ele enxerga — que tudo foi apagado.
        foreach (var (apelido, vault) in VaultsNoDisco())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Escopo próprio POR USUÁRIO: o DbContext é scoped e este serviço é singleton (resolver o
                // contexto do provedor raiz é o clássico "captured dependency", que funciona no teste e
                // vaza conexão em produção). Aqui o escopo tem uma segunda função — declarar de quem é o
                // vault, já que não há ninguém logado para perguntar.
                using var escopo = _escopos.CreateScope();
                escopo.ServiceProvider.GetRequiredService<EscopoDoUsuario>().Definir(apelido, vault);
                await escopo.ServiceProvider.GetRequiredService<ReconciliarVault>().ExecutarAsync(ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                // O vault de um não pode impedir a reconciliação do outro. Sem este catch, uma nota
                // ilegível na pasta de alguém deixaria todo mundo depois dele fora de sincronia.
                _log.LogError(e, "Falha ao reconciliar {Vault} de {Apelido}; seguindo para os demais.", vault, apelido);
            }
        }
    }

    /// <summary>
    /// Todos os vaults do disco, como pares (pessoa, vault) — DOIS níveis de pasta:
    /// <c>{raiz}/{apelido}/{vault}</c>.
    /// </summary>
    private IEnumerable<(ApelidoDoUsuario Apelido, NomeDoVault Vault)> VaultsNoDisco()
    {
        var raiz = Path.GetFullPath(_opcoes.Raiz);
        IEnumerable<string> pastas;
        try { pastas = Directory.EnumerateDirectories(raiz); }
        catch (DirectoryNotFoundException) { yield break; }

        foreach (var pasta in pastas)
        {
            var nome = Path.GetFileName(pasta);
            if (!ApelidoDoUsuario.TentarCriar(nome, out var apelido, out var erro) || apelido is null)
            {
                // Pasta que não é de ninguém — um ".git" na raiz, uma sobra de cópia. Avisa em vez de
                // ignorar calado: se for a pasta de alguém com nome errado, o dono precisa saber por que
                // as notas dele não aparecem.
                _log.LogWarning("Pasta na raiz do vault que não é de nenhum usuário ({Motivo}): {Pasta}", erro, nome);
                continue;
            }

            foreach (var subpasta in Directory.EnumerateDirectories(pasta))
            {
                var nomeDoVault = Path.GetFileName(subpasta);
                if (!NomeDoVault.TentarCriar(nomeDoVault, out var vault, out var porQue) || vault is null)
                {
                    // Mesmo critério do nível de cima, e o aviso importa ainda mais aqui: uma MATÉRIA
                    // deixada por engano na pasta do usuário (migração que não rodou, cópia manual) cai
                    // exatamente neste ramo, e o dono veria o vault sem aquela matéria e nada mais.
                    _log.LogWarning("Pasta em {Apelido} que não é um vault válido ({Motivo}): {Pasta}",
                        apelido, porQue, nomeDoVault);
                    continue;
                }
                yield return (apelido, vault);
            }
        }
    }

    public override void Dispose()
    {
        _vigia?.Dispose();
        _acordar.Dispose();
        base.Dispose();
    }
}
