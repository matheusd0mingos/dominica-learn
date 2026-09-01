using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Web.Data;

/// <summary>
/// Uma turma. Mora no banco da identidade, junto do acompanhamento — pelo mesmo motivo dele.
///
/// O CÓDIGO É A CHAVE. Não há id numérico por baixo: o código já é único, já é o que a pessoa digita e
/// já é o que aparece na URL. Um id separado criaria duas maneiras de apontar para a mesma turma, e a
/// segunda existiria só para ser confundida com a primeira.
/// </summary>
[PrimaryKey(nameof(Codigo))]
public class TurmaNoBanco
{
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Quem criou — é ele quem acompanha os alunos.</summary>
    public string Dono { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    public DateTimeOffset CriadaEm { get; set; }
}

/// <summary>
/// Um aluno dentro de uma turma.
///
/// GUARDA O VAULT porque é dele que o acompanhamento fala. O mesmo aluno pode estar numa turma pelo
/// vault de estudo e ter outro vault que a turma não vê — e a lista do professor precisa saber qual
/// painel abrir, não adivinhar.
/// </summary>
[PrimaryKey(nameof(Turma), nameof(Aluno))]
public class MatriculaNoBanco
{
    public string Turma { get; set; } = string.Empty;
    public string Aluno { get; set; } = string.Empty;
    public string Vault { get; set; } = string.Empty;
    public DateTimeOffset EntrouEm { get; set; }
}
