using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Compartilhamento;

/// <summary>
/// <c>{dono}</c> deixa <c>{convidado}</c> acompanhar como vão os estudos dele — horas, questões,
/// acerto, retenção. É o que faz um professor ver a turma e dois amigos verem um ao outro.
///
/// SÓ LEITURA, E SEM PAPEL NENHUM PARA ESCOLHER. A <see cref="Concessao"/> das notas tem Leitor e
/// Editor porque um grupo pode montar material junto; aqui não existe o equivalente. Ninguém registra
/// estudo no lugar de outra pessoa: o registro é a prova do que ELA fez, e um professor que pudesse
/// lançar horas na conta do aluno transformaria o painel numa ficha de avaliação — que é outro produto,
/// com outras garantias. Um enum de um valor só é um enum que vai ganhar o segundo valor sem ninguém
/// pensar duas vezes.
///
/// É POR VAULT, e não por pessoa. Uma pessoa pode ter o vault de estudo e o de trabalho, e o registro
/// já é separado por eles (o filtro do <c>ContextoDoRegistro</c> é por <c>(Usuario, Vault)</c>).
/// Compartilhar "meus estudos" sem dizer qual vault entregaria os dois — e o que a pessoa quis mostrar
/// foi um. Na prática quem só tem um vault nunca vê esta distinção; quem tem dois é justamente quem
/// seria prejudicado por ela não existir.
///
/// MORA COM A IDENTIDADE, pela mesma razão escrita na <see cref="Concessao"/>: não é conhecimento do
/// usuário. No índice, um "reindexar do zero" revogaria o acesso de todo mundo; no vault, quem
/// recebesse uma cópia dos arquivos receberia junto a lista de quem o observa.
/// </summary>
public sealed record Acompanhamento
{
    public ApelidoDoUsuario Dono { get; }
    public NomeDoVault Vault { get; }
    public ApelidoDoUsuario Convidado { get; }

    private Acompanhamento(ApelidoDoUsuario dono, NomeDoVault vault, ApelidoDoUsuario convidado)
    {
        Dono = dono;
        Vault = vault;
        Convidado = convidado;
    }

    /// <summary>
    /// Devolve null quando o acompanhamento não faria sentido — e falhar aqui é o contrário de gravar
    /// uma regra de acesso malformada que depois vale mais ou menos.
    /// </summary>
    public static Acompanhamento? TentarCriar(
        ApelidoDoUsuario? dono, NomeDoVault? vault, ApelidoDoUsuario? convidado)
    {
        if (dono is null || vault is null || convidado is null) return null;

        // ACOMPANHAR A SI MESMO não é um caso a tratar mais adiante: é uma linha sem significado que
        // faria a lista de "quem me acompanha" mentir, e que daria a alguém a impressão de estar sendo
        // observado por si próprio. Todo mundo já vê o próprio painel — é a tela inicial.
        if (dono == convidado) return null;

        return new Acompanhamento(dono, vault, convidado);
    }
}
