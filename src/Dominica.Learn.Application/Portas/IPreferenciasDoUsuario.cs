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

    /// <summary>
    /// Em qual vault a pessoa estava da última vez. Nulo = nunca escolheu.
    ///
    /// ISTO É PREFERÊNCIA, E NÃO FRONTEIRA, e a distinção é o que decide onde o dado mora. A fronteira —
    /// "de quem é este vault" — é o apelido, e ele é imutável e vale para a segurança. Qual vault está
    /// aberto é onde a pessoa parou de trabalhar: some, e o pior que acontece é ela abrir no estudo
    /// quando queria o trabalho, e trocar num clique.
    ///
    /// Por isso fica aqui e não no cookie: trocar de máquina não devia recomeçar no vault errado.
    /// </summary>
    Task<Dominica.Learn.Domain.Vault.NomeDoVault?> VaultAtualAsync(CancellationToken ct = default);

    Task DefinirVaultAtualAsync(Dominica.Learn.Domain.Vault.NomeDoVault vault, CancellationToken ct = default);
}
