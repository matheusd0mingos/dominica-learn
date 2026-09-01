using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>Um aluno dentro de uma turma, e se o acesso dele ainda está de pé.</summary>
/// <param name="Acesso">
/// FALSO QUANDO O ALUNO REVOGOU no painel dele. A turma não perde o membro por causa disso, e mostrar
/// a lista como se estivesse tudo bem seria mentir para o professor — ele veria o nome, clicaria e
/// tomaria "sem acesso" sem entender. A turma organiza convites; quem manda no acesso é o dono do
/// painel, sempre, e a lista precisa dizer a verdade sobre isso.
/// </param>
public sealed record AlunoDaTurma(ApelidoDoUsuario Aluno, NomeDoVault Vault, DateTimeOffset EntrouEm, bool Acesso);

/// <summary>
/// Onde as turmas e as matrículas ficam guardadas.
///
/// SEPARADA DA PORTA DE ACOMPANHAMENTO de propósito, embora as duas vivam no mesmo banco: são
/// autoridades diferentes. O acompanhamento AUTORIZA; a turma só ORGANIZA. Juntá-las numa porta só
/// convidaria, no primeiro caso especial, a uma consulta que lê a turma para decidir acesso — e aí a
/// turma vira autoridade sem ninguém ter decidido isso.
/// </summary>
public interface ITurmas
{
    Task CriarAsync(Turma turma, CancellationToken ct = default);

    /// <summary>Null quando o código não existe. Não distingue "nunca existiu" de "foi apagada".</summary>
    Task<Turma?> PorCodigoAsync(CodigoDeTurma codigo, CancellationToken ct = default);

    Task<IReadOnlyList<Turma>> DeQuemAsync(ApelidoDoUsuario dono, CancellationToken ct = default);

    /// <summary>As turmas em que EU entrei — a lista do lado do aluno.</summary>
    Task<IReadOnlyList<Turma>> EmQueEstouAsync(ApelidoDoUsuario aluno, CancellationToken ct = default);

    /// <summary>Idempotente: entrar duas vezes na mesma turma não cria duas matrículas.</summary>
    Task MatricularAsync(CodigoDeTurma codigo, ApelidoDoUsuario aluno, NomeDoVault vault, CancellationToken ct = default);

    Task<bool> DesmatricularAsync(CodigoDeTurma codigo, ApelidoDoUsuario aluno, CancellationToken ct = default);

    /// <summary>A lista do professor, já dizendo de quem o acesso caiu.</summary>
    Task<IReadOnlyList<AlunoDaTurma>> AlunosAsync(CodigoDeTurma codigo, CancellationToken ct = default);

    /// <summary>Apaga a turma e as matrículas dela. NÃO mexe nos acompanhamentos — ver <see cref="Turma"/>.</summary>
    Task<bool> ApagarAsync(CodigoDeTurma codigo, ApelidoDoUsuario dono, CancellationToken ct = default);
}
