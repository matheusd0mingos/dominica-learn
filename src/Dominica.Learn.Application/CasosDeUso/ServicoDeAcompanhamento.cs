using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

/// <summary>
/// Quem pode acompanhar os estudos de quem — e a leitura do painel alheio, quando pode.
///
/// TODA LEITURA CRUZADA PASSA POR AQUI. É a única classe que chama o <see cref="IEstudoDeOutraPessoa"/>,
/// e ela nunca o chama sem antes perguntar ao <see cref="IAcompanhamentosDeEstudo"/>. Espalhar a
/// checagem pelas telas seria o desenho em que a próxima tela esquece — e a tela que esquece não dá
/// erro: ela mostra o painel de alguém para quem não devia.
/// </summary>
public sealed class ServicoDeAcompanhamento(
    IAcompanhamentosDeEstudo acompanhamentos,
    IEstudoDeOutraPessoa estudoAlheio,
    IUsuarioAtual usuario,
    ILogger<ServicoDeAcompanhamento> log)
{
    /// <summary>
    /// Deixa alguém acompanhar o vault em que EU estou agora.
    ///
    /// O DONO É SEMPRE QUEM ESTÁ LOGADO, e não um parâmetro. Receber o dono de fora deixaria a porta
    /// aberta para conceder acesso ao painel de terceiros — o convite tem de partir de quem é dono do
    /// que está sendo mostrado.
    /// </summary>
    public async Task<Resultado<Acompanhamento>> ConvidarAsync(string? apelidoConvidado, CancellationToken ct = default)
    {
        var dono = await usuario.ApelidoAsync(ct);
        var vault = await usuario.VaultAsync(ct);

        if (!ApelidoDoUsuario.TentarCriar(apelidoConvidado, out var convidado, out var erro) || convidado is null)
            return Resultado<Acompanhamento>.Invalida(erro ?? "Apelido inválido.");

        var acompanhamento = Acompanhamento.TentarCriar(dono, vault, convidado);
        if (acompanhamento is null)
            return Resultado<Acompanhamento>.Invalida(dono == convidado
                // A mensagem diz o caso, e não "inválido": quem digitou o próprio apelido merece saber
                // por que não deu, e a resposta é engraçada o bastante para ser dita por extenso.
                ? "Você já vê o seu próprio painel — é a tela inicial."
                : "Não consegui montar o convite.");

        await acompanhamentos.ConcederAsync(acompanhamento, ct);
        log.LogInformation("{Dono} deixou {Convidado} acompanhar o vault {Vault}", dono.Valor, convidado.Valor, vault.Valor);
        return Resultado<Acompanhamento>.Sucesso(acompanhamento);
    }

    /// <summary>Tira o acesso de alguém ao MEU vault atual. Também aqui o dono é quem está logado.</summary>
    public async Task<bool> RevogarAsync(string? apelidoConvidado, CancellationToken ct = default)
    {
        var dono = await usuario.ApelidoAsync(ct);
        var vault = await usuario.VaultAsync(ct);
        if (!ApelidoDoUsuario.TentarCriar(apelidoConvidado, out var convidado, out _) || convidado is null) return false;

        var acompanhamento = Acompanhamento.TentarCriar(dono, vault, convidado);
        if (acompanhamento is null) return false;

        var tirou = await acompanhamentos.RevogarAsync(acompanhamento, ct);
        if (tirou) log.LogInformation("{Dono} revogou o acompanhamento de {Convidado}", dono.Valor, convidado.Valor);
        return tirou;
    }

    /// <summary>Quem eu deixei acompanhar o vault em que estou.</summary>
    public async Task<IReadOnlyList<Acompanhamento>> QuemMeAcompanhaAsync(CancellationToken ct = default) =>
        await acompanhamentos.QuemMeAcompanhaAsync(await usuario.ApelidoAsync(ct), await usuario.VaultAsync(ct), ct);

    /// <summary>Os painéis que eu posso abrir.</summary>
    public async Task<IReadOnlyList<Acompanhamento>> QueEuAcompanhoAsync(CancellationToken ct = default) =>
        await acompanhamentos.QueEuAcompanhoAsync(await usuario.ApelidoAsync(ct), ct);

    /// <summary>
    /// O painel de outra pessoa — <b>se</b> ela me deixou ver.
    ///
    /// DEVOLVE null QUANDO NÃO PODE, e é o chamador que traduz isso em tela. Uma exceção seria pior:
    /// negar acesso é resposta normal deste método (a permissão pode ter sido revogada entre a lista e
    /// o clique), e transformar o normal em exceção ensina a envolver a chamada num try/catch que engole
    /// junto os erros de verdade.
    /// </summary>
    public async Task<T?> PainelDeAsync<T>(
        ApelidoDoUsuario dono, NomeDoVault vault,
        Func<ServicoDeDesempenho, Task<T>> leitura, CancellationToken ct = default) where T : class
    {
        var eu = await usuario.ApelidoAsync(ct);

        // A CHECAGEM É AQUI, ANTES, E CONTRA O BANCO — não contra uma lista que a tela carregou minutos
        // atrás. Entre abrir a lista e clicar num nome, o acesso pode ter sido revogado; quem decide é
        // o estado de agora.
        if (!await acompanhamentos.PodeVerAsync(eu, dono, vault, ct))
        {
            log.LogWarning("{Quem} tentou abrir o painel de {Dono}/{Vault} sem acompanhamento", eu.Valor, dono.Valor, vault.Valor);
            return null;
        }

        return await estudoAlheio.LendoComoAsync(dono, vault, leitura, ct);
    }
}
