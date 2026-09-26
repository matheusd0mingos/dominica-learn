using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>
/// Onde ficam guardados os links públicos de nota.
///
/// MORA COM A IDENTIDADE, e não no índice do vault, pelo mesmo motivo do
/// <see cref="IAcompanhamentosDeEstudo"/>: isto é autorização. O índice é reconstruível — a documentação
/// manda reindexar do zero quando algo está estranho — e uma reindexação que apagasse os links deixaria
/// mortas as URLs que já foram mandadas por e-mail, sem ninguém entender por quê. Pior ainda seria o
/// caminho inverso: um índice recriado a partir do disco não teria como recriar os tokens, e "recriar"
/// aqui significaria sortear outros.
///
/// A BUSCA POR TOKEN É A ÚNICA QUE NÃO PEDE DONO, e é de propósito: do outro lado do link não há usuário
/// nenhum para perguntar. É essa assimetria que faz o <see cref="ServicoDeLinksDeNota"/> ser o único
/// lugar onde ela pode ser chamada.
/// </summary>
public interface ILinksDeNota
{
    /// <summary>
    /// Publica. IDEMPOTENTE POR (dono, vault, caminho): republicar a mesma nota devolve o link que já
    /// existe, em vez de sortear um segundo token.
    ///
    /// A ALTERNATIVA É PERIGOSA DE UM JEITO CALADO: com dois tokens vivos para a mesma nota, quem
    /// revogasse "o link" pela tela mataria um e deixaria o outro funcionando — e a pessoa sairia dali
    /// convencida de que fechou o acesso.
    /// </summary>
    Task<LinkDeNota> PublicarAsync(LinkDeNota link, CancellationToken ct = default);

    /// <summary>O link daquele token, ou null. É por aqui que a rota pública entra — e ela não sabe de quem é.</summary>
    Task<LinkDeNota?> PorTokenAsync(TokenDeLink token, CancellationToken ct = default);

    /// <summary>O link desta nota, se ela está publicada — o que a tela precisa para mostrar "copiar" ou "publicar".</summary>
    Task<LinkDeNota?> DaNotaAsync(ApelidoDoUsuario dono, NomeDoVault vault, CaminhoNota caminho, CancellationToken ct = default);

    /// <summary>Tudo que eu publiquei, do vault mais recente para o mais antigo. Sem esta lista, publicar é irreversível na prática: ninguém revoga o que não consegue ver.</summary>
    Task<IReadOnlyList<LinkDeNota>> MeusAsync(ApelidoDoUsuario dono, CancellationToken ct = default);

    /// <summary>Mata o link. O dono vai no WHERE — revogar é ato de quem publicou.</summary>
    Task<bool> RevogarAsync(ApelidoDoUsuario dono, TokenDeLink token, CancellationToken ct = default);
}
