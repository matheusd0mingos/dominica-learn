namespace Dominica.Learn.Application.Portas;

/// <summary>
/// As preferências de quem está estudando.
///
/// PORTA SEPARADA DE <see cref="IUsuarioAtual"/> de propósito: aquela responde "de quem é este vault?" e
/// é a fronteira entre pessoas — a classe mais sensível do sistema. Preferência é ajuste de conforto.
/// Juntar as duas faria toda mudança de preferência passar pela classe que não pode ter mudança
/// nenhuma sem prova.
///
/// Fica na camada de aplicação porque quem precisa delas são os casos de uso; quem sabe onde elas moram
/// (a tabela de identidade) é a camada Web, que já conhece o Identity.
/// </summary>
public interface IPreferenciasDoUsuario
{
    /// <summary>Quantos cartões inéditos podem entrar na revisão por dia. Zero = sem teto.</summary>
    Task<int> CartoesNovosPorDiaAsync(CancellationToken ct = default);

    Task DefinirCartoesNovosPorDiaAsync(int quantos, CancellationToken ct = default);
}
