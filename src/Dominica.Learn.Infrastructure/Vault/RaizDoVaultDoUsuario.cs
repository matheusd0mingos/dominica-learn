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
        return _resolvida = Criar(RaizComum, apelido);
    }

    /// <summary>
    /// A pasta de um usuário qualquer. Usado pelo vigia, que trabalha fora de qualquer sessão.
    /// </summary>
    public static string Criar(string raizComum, ApelidoDoUsuario apelido)
    {
        // O apelido já é validado pelo domínio como um segmento seguro — sem barra, sem "..", sem
        // acento. Ainda assim a raiz passa pela mesma conferência de escape de todo o resto: é a última
        // linha antes de uma chamada de sistema de arquivos, e conferir aqui custa microssegundos.
        var raiz = CaminhoSeguro.Combinar(Path.GetFullPath(raizComum), apelido.Valor, apelido.Valor);
        Directory.CreateDirectory(raiz);
        return CaminhoSeguro.Real(raiz);
    }
}
