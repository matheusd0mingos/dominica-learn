using Microsoft.AspNetCore.Identity;

namespace Dominica.Learn.Web.Data;

/// <summary>
/// O usuário do Learn.
///
/// <see cref="Apelido"/> é o único campo próprio, e ele é estrutural: é o nome da PASTA do vault desta
/// pessoa no disco (/dados/vault/matheus/…) e, por consequência, a fronteira entre o conhecimento dela e
/// o de qualquer outra. Não é apelido de exibição.
///
/// Por isso ele é IMUTÁVEL depois do cadastro. Trocá-lo significaria mover a árvore inteira no disco e
/// reescrever o caminho de toda linha do índice e do histórico, possivelmente com o Obsidian de alguém
/// aberto no meio. As regras do que é um apelido válido moram em
/// <see cref="Dominica.Learn.Domain.Vault.ApelidoDoUsuario"/> — aqui só se guarda o resultado.
///
/// AS PREFERÊNCIAS TAMBÉM MORAM AQUI, e não no vault. A regra do projeto manda o conhecimento do usuário
/// para o disco; preferência não é conhecimento — perdê-la custa reconfigurar, não reaprender. E o vault
/// seria um lugar ruim para ela: um arquivo comum poluiria a lista de notas, e um arquivo oculto ficaria
/// de fora do backup, que ignora tudo que começa com ponto.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string Apelido { get; set; } = string.Empty;

    /// <summary>
    /// Quantos cartões INÉDITOS podem entrar na revisão por dia. Zero = sem teto.
    ///
    /// É o parâmetro que mais decide se a rotina sobrevive — ver
    /// <see cref="Dominica.Learn.Domain.Cartoes.TetoDeCartoesNovos"/>.
    /// </summary>
    public int CartoesNovosPorDia { get; set; } = Dominica.Learn.Domain.Cartoes.TetoDeCartoesNovos.Padrao;
}
