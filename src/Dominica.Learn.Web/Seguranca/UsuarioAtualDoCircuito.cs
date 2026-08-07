using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Microsoft.AspNetCore.Components.Authorization;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// Adaptador de <see cref="IUsuarioAtual"/> sobre a autenticação do circuito do Blazor.
///
/// Todo o resto do sistema — casos de uso, repositório, índice — recebe um apelido e trabalha, sem saber
/// o que é sessão.
///
/// A ORDEM DA CONSULTA IMPORTA: primeiro a seleção explícita do escopo, depois a autenticação. A seleção
/// só tem valor quando alguém a colocou de propósito, e hoje quem faz isso é o vigia do vault — que
/// reconcilia o disco de todo mundo em segundo plano, sem circuito, sem cookie e sem ninguém logado — e
/// o endpoint do download do pacote, que tem <c>HttpContext</c> e nenhum circuito para perguntar.
///
/// É POR ISSO QUE ESTA CLASSE NÃO OLHA O <c>HttpContext</c> sozinha. O acessador é ambiente: dentro de um
/// circuito ele vem nulo, e nada garante que um dia não venha o contexto de outra requisição. Um engano
/// desses aqui não daria erro — entregaria o vault de outra pessoa. Quem tem certeza de quem é o dono
/// declara no <see cref="EscopoDoUsuario"/>; aqui só se responde pelo circuito.
/// </summary>
public sealed class UsuarioAtualDoCircuito(
    EscopoDoUsuario escopo,
    AuthenticationStateProvider autenticacao,
    ApelidoDeQuemEntrou apelidos,
    IPreferenciasDoUsuario preferencias) : IUsuarioAtual
{
    private ApelidoDoUsuario? _resolvido;
    private NomeDoVault? _vault;

    public async Task<ApelidoDoUsuario> ApelidoAsync(CancellationToken ct = default)
    {
        if (escopo.Definido is { } declarado) return declarado;
        if (_resolvido is not null) return _resolvido;

        var estado = await autenticacao.GetAuthenticationStateAsync();
        return _resolvido = await apelidos.DeAsync(estado.User);
    }

    /// <summary>
    /// MESMA ORDEM DO APELIDO — escopo declarado primeiro, sessão depois — e pelo mesmo motivo: o vigia
    /// reconcilia vaults sem circuito nenhum e é ele quem sabe em qual está trabalhando.
    ///
    /// GUARDA NO ESCOPO DEPOIS DE RESOLVER. O vault é perguntado em toda operação de arquivo e de
    /// índice; sem isto, cada uma custaria uma consulta à tabela de identidade. Dentro de um circuito o
    /// vault só muda por navegação, que abre tudo de novo — ver TrocaDeVault.
    /// </summary>
    public async Task<NomeDoVault> VaultAsync(CancellationToken ct = default)
    {
        if (escopo.VaultDefinido is { } declarado) return declarado;
        if (_vault is not null) return _vault;

        // Quem nunca escolheu está no padrão — que é para onde a migração levou o que já existia.
        return _vault = await preferencias.VaultAtualAsync(ct) ?? NomeDoVault.Padrao;
    }
}
