using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>
/// QUEM ESTÁ ESTUDANDO AGORA. É a fronteira entre o conhecimento de uma pessoa e o de outra.
///
/// Esta porta existe para que a decisão "de quem é este vault?" seja feita UMA vez, num lugar, e não
/// espalhada em cada consulta — porque uma consulta esquecida não falha, ela devolve a nota de outra
/// pessoa. Isso é vazamento silencioso, o tipo de defeito que só aparece quando o dano já foi feito.
///
/// O adaptador vive na camada Web e lê o estado de autenticação do circuito. O domínio e os casos de uso
/// nunca souberam o que é um cookie, e continuam não sabendo.
///
/// A ausência de usuário é EXCEÇÃO aqui, e não Result: chegar num caso de uso sem usuário autenticado
/// significa que a página esqueceu o [Authorize] ou que um serviço de fundo chamou algo que só faz
/// sentido para gente. Nos dois casos é defeito de programa, não fluxo esperado — e falhar alto é o
/// contrário de devolver o vault de outra pessoa por engano.
/// </summary>
public interface IUsuarioAtual
{
    /// <summary>O apelido de quem está autenticado. Lança se não houver ninguém.</summary>
    Task<ApelidoDoUsuario> ApelidoAsync(CancellationToken ct = default);

    /// <summary>
    /// EM QUAL VAULT a pessoa está trabalhando agora — estudo, trabalho, o que ela tiver criado.
    ///
    /// A FRONTEIRA VIROU UM PAR. Era só o apelido; agora é (apelido, vault), e todo lugar que filtrava
    /// por um passou a filtrar pelos dois. O motivo não é organização: é o wikilink. "[[...]]" resolve
    /// por nome de arquivo no vault inteiro, então um "[[Aterramento]]" escrito estudando podia apontar
    /// para a nota de engenharia — e ninguém vê isso acontecer, vê um backlink estranho meses depois.
    /// Ligação errada dentro do conhecimento é o defeito mais caro daqui, porque o conhecimento é o
    /// produto.
    ///
    /// NUNCA LANÇA E NUNCA VOLTA VAZIO: quem nunca escolheu está em <see cref="NomeDoVault.Padrao"/>,
    /// que é para onde a migração levou o que já existia. Um vault indefinido não tem significado — não
    /// existe nota fora de vault nenhum.
    /// </summary>
    Task<NomeDoVault> VaultAsync(CancellationToken ct = default);
}

/// <summary>
/// De quem é o vault DESTE escopo, quando não há ninguém logado para perguntar.
///
/// Existe por causa do vigia do vault: ele reconcilia o disco de todo mundo em segundo plano, sem
/// circuito e sem cookie, e ainda assim precisa que o repositório e o índice trabalhem no vault de UMA
/// pessoa por vez. Quem abre o escopo declara aqui de quem ele é.
///
/// Fica vazio no uso normal — aí quem responde é a autenticação. É por isso que o adaptador consulta
/// esta seleção PRIMEIRO: um valor aqui só existe porque alguém o colocou de propósito.
/// </summary>
public sealed class EscopoDoUsuario
{
    public ApelidoDoUsuario? Definido { get; private set; }

    /// <summary>
    /// O vault deste escopo. O vigia reconcilia um VAULT por vez, não um usuário por vez — cada vault
    /// tem índice próprio, e reconciliar "o usuário" sem dizer qual vault misturaria os dois.
    /// </summary>
    public NomeDoVault? VaultDefinido { get; private set; }

    public void Definir(ApelidoDoUsuario apelido, NomeDoVault? vault = null)
    {
        Definido = apelido;
        VaultDefinido = vault;
    }
}

/// <summary>Não havia usuário autenticado onde o código pressupôs que houvesse.</summary>
public sealed class SemUsuarioAutenticadoException()
    : InvalidOperationException(
        "Nenhum usuário autenticado. Toda leitura e gravação do vault pertence a alguém — " +
        "verifique se a página tem [Authorize] e se o endpoint exige autorização.");
