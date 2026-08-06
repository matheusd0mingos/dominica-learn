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
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string Apelido { get; set; } = string.Empty;
}
