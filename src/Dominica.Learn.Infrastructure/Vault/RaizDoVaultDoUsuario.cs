using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Options;

namespace Dominica.Learn.Infrastructure.Vault;

/// <summary>
/// Onde fica o vault de quem está usando o sistema agora: <c>{Vault:Raiz}/{apelido}</c>.
///
/// É A FRONTEIRA ENTRE AS PESSOAS, e ela é UMA linha de código — de propósito. A alternativa seria cada
/// consulta lembrar de filtrar por usuário, e o problema disso é que uma consulta esquecida não quebra:
/// ela devolve a nota de outra pessoa, em silêncio, e ninguém descobre até o dano estar feito. Aqui, um
/// adaptador que esquecesse de pedir a raiz simplesmente não teria caminho nenhum para escrever.
///
/// Resolve uma vez por escopo (circuito do Blazor, ou o escopo que o vigia abre para reconciliar um
/// usuário). Dentro de um escopo o usuário não muda, então guardar o resultado é seguro e evita uma
/// consulta de autenticação por operação de arquivo.
/// </summary>
public sealed class RaizDoVaultDoUsuario(IOptions<OpcoesDoVault> opcoes, IUsuarioAtual usuario)
{
    private string? _resolvida;

    /// <summary>A raiz comum a todos os vaults. Só o vigia tem motivo para conhecê-la.</summary>
    public string RaizComum => Path.GetFullPath(opcoes.Value.Raiz);

    public async Task<string> ObterAsync(CancellationToken ct = default)
    {
        if (_resolvida is not null) return _resolvida;

        var apelido = await usuario.ApelidoAsync(ct);
        var vault = await usuario.VaultAsync(ct);
        return _resolvida = Criar(RaizComum, apelido, vault);
    }

    /// <summary>A pasta de um usuário — a que CONTÉM os vaults dele, não um vault.</summary>
    public static string PastaDoUsuario(string raizComum, ApelidoDoUsuario apelido) =>
        CaminhoSeguro.Combinar(Path.GetFullPath(raizComum), apelido.Valor, apelido.Valor);

    /// <summary>
    /// A raiz de um vault qualquer. Usado pelo vigia, que trabalha fora de qualquer sessão.
    ///
    /// DOIS SEGMENTOS, E NÃO UM. Era <c>{raiz}/{apelido}</c>; virou <c>{raiz}/{apelido}/{vault}</c>, e
    /// esta linha é toda a separação entre o que a pessoa estuda e o que ela anota de trabalho. O
    /// mesmo argumento que fez a fronteira entre PESSOAS ser uma linha só vale aqui: uma consulta que
    /// esquecesse de filtrar não falharia, devolveria a nota do outro vault — em silêncio. Aqui, quem
    /// não pedir a raiz não tem caminho nenhum para escrever.
    /// </summary>
    public static string Criar(string raizComum, ApelidoDoUsuario apelido, NomeDoVault vault)
    {
        // Apelido e nome de vault já são validados pelo domínio como segmentos seguros — sem barra, sem
        // "..", sem acento. Ainda assim passam pela mesma conferência de escape de todo o resto: é a
        // última linha antes de uma chamada de sistema de arquivos, e conferir custa microssegundos.
        var raiz = CaminhoSeguro.Combinar(
            Path.GetFullPath(raizComum), $"{apelido.Valor}/{vault.Valor}", $"{apelido}/{vault}");
        Directory.CreateDirectory(raiz);
        return CaminhoSeguro.Real(raiz);
    }

    /// <summary>
    /// Os vaults que existem no disco para uma pessoa, em ordem alfabética.
    ///
    /// SAI DO DISCO, e não de uma tabela. É a mesma escolha da matéria (que é a pasta): criar uma pasta
    /// ali dentro pelo Obsidian ou pelo terminal JÁ é criar um vault, sem nada para sincronizar. Uma
    /// tabela de vaults teria de ser reconciliada com o disco, e o dia em que as duas discordassem seria
    /// o dia em que um vault existiria e não apareceria.
    ///
    /// Pasta com nome que não vale como vault é ignorada em silêncio aqui — quem avisa é o vigia, que
    /// tem log. Esta lista alimenta uma tela, e tela não é lugar de reclamar de arquivo.
    /// </summary>
    public static IReadOnlyList<NomeDoVault> VaultsDe(string raizComum, ApelidoDoUsuario apelido)
    {
        var pasta = Path.Combine(Path.GetFullPath(raizComum), apelido.Valor);
        if (!Directory.Exists(pasta)) return [];

        return Directory.EnumerateDirectories(pasta)
            .Select(d => NomeDoVault.Conhecido(Path.GetFileName(d)))
            .OfType<NomeDoVault>()
            .OrderBy(v => v.Valor, StringComparer.Ordinal)
            .ToList();
    }
}
