using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>
/// Onde ficam guardadas as permissões de acompanhar o painel de alguém.
///
/// PORTA SEPARADA DO REGISTRO DE ESTUDO, e não mais um método nele: são bancos diferentes por um motivo
/// que importa. O registro é do usuário e vive no banco do registro, filtrado por <c>(Usuario, Vault)</c>;
/// a autorização diz QUEM pode atravessar esse filtro, e uma autorização guardada atrás do filtro que
/// ela mesma governa é uma porta cuja chave está do lado de dentro.
/// </summary>
public interface IAcompanhamentosDeEstudo
{
    /// <summary>Idempotente: conceder duas vezes o mesmo acompanhamento não cria duas linhas.</summary>
    Task ConcederAsync(Acompanhamento acompanhamento, CancellationToken ct = default);

    /// <summary>Devolve false quando não havia o que revogar — revogar o que não existe não é erro.</summary>
    Task<bool> RevogarAsync(Acompanhamento acompanhamento, CancellationToken ct = default);

    /// <summary>Quem o dono deixou acompanhar ESTE vault dele. É a lista que ele administra.</summary>
    Task<IReadOnlyList<Acompanhamento>> QuemMeAcompanhaAsync(
        ApelidoDoUsuario dono, NomeDoVault vault, CancellationToken ct = default);

    /// <summary>
    /// Os painéis que o convidado pode abrir. Sem filtro de vault de propósito: é a lista da tela
    /// "acompanhando", e ali cada linha é um par (pessoa, vault) que alguém abriu para ele.
    /// </summary>
    Task<IReadOnlyList<Acompanhamento>> QueEuAcompanhoAsync(
        ApelidoDoUsuario convidado, CancellationToken ct = default);

    /// <summary>
    /// A PERGUNTA QUE AUTORIZA, e a única que a leitura cruzada deve fazer.
    ///
    /// Existe como método próprio, em vez de "buscar a lista e procurar dentro", porque quem autoriza
    /// não pode depender de o chamador ter filtrado certo. Uma checagem escrita na tela é uma checagem
    /// que a próxima tela esquece.
    /// </summary>
    Task<bool> PodeVerAsync(
        ApelidoDoUsuario convidado, ApelidoDoUsuario dono, NomeDoVault vault, CancellationToken ct = default);
}
