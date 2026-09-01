using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Web.Data;

/// <summary>
/// A linha que diz "fulano pode acompanhar os estudos de beltrano no vault X".
///
/// MORA NO BANCO DA IDENTIDADE, junto do usuário, e não no banco do registro. O registro é filtrado por
/// <c>(Usuario, Vault)</c>; guardar ali a regra que autoriza atravessar esse filtro seria pôr a chave do
/// lado de dentro da porta — para ler a permissão de ver o registro de alguém, seria preciso já estar
/// lendo o registro dessa pessoa.
///
/// GUARDA APELIDOS EM TEXTO, e não chaves estrangeiras para AspNetUsers. O apelido é imutável por
/// contrato (ver <see cref="ApplicationUser"/>) e é a mesma chave que o registro usa na coluna
/// <c>Usuario</c> — casar por Id aqui e por apelido lá criaria duas noções de "quem", e a divergência
/// entre elas apareceria como uma permissão que existe mas não funciona.
/// </summary>
[PrimaryKey(nameof(Dono), nameof(Vault), nameof(Convidado))]
public class AcompanhamentoNoBanco
{
    /// <summary>De quem é o painel.</summary>
    public string Dono { get; set; } = string.Empty;

    /// <summary>Qual vault dele — o registro é separado por vault, e o convite também.</summary>
    public string Vault { get; set; } = string.Empty;

    /// <summary>Quem ganhou permissão de olhar.</summary>
    public string Convidado { get; set; } = string.Empty;

    /// <summary>
    /// Quando o acesso foi dado. Não é enfeite: é o que responde "desde quando essa pessoa vê os meus
    /// estudos?" numa tela onde a resposta importa — e é barato guardar agora, caro reconstruir depois.
    /// </summary>
    public DateTimeOffset Em { get; set; }
}
