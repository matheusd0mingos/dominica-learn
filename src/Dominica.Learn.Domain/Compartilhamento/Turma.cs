using System.Security.Cryptography;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Compartilhamento;

/// <summary>
/// O código que um aluno digita para entrar numa turma.
///
/// NÃO É SEGREDO, É ENDEREÇO. Quem tem o código não ganha acesso a nada: ganha o direito de OFERECER o
/// próprio painel ao dono da turma. O risco de adivinhar um código não é ler os estudos de alguém — é
/// entregar os seus a um desconhecido, e por isso a tela mostra de QUEM é a turma antes de confirmar.
///
/// Ainda assim é sorteado com <see cref="RandomNumberGenerator"/> e não com <c>Random</c>: código
/// previsível permitiria enumerar as turmas que existem, e a lista de turmas de uma escola já diz mais
/// do que deveria sobre quem estuda onde.
///
/// O ALFABETO OMITE 0/O e 1/I/L de propósito. Este código é ditado por voz e copiado à mão de um quadro;
/// os pares que se confundem viram "o código não funciona" — e quem não consegue entrar na turma não
/// reclama, desiste.
/// </summary>
public sealed record CodigoDeTurma
{
    private const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Tamanho = 8;

    public string Valor { get; }

    private CodigoDeTurma(string valor) => Valor = valor;

    public static CodigoDeTurma Sortear()
    {
        var letras = new char[Tamanho];
        for (var i = 0; i < Tamanho; i++) letras[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        return new CodigoDeTurma(new string(letras));
    }

    /// <summary>
    /// Aceita o que a pessoa digitou: minúsculas viram maiúsculas, espaços e hifens somem. Quem copia de
    /// um quadro escreve "abcd-2345", e recusar isso seria recusar o uso normal.
    /// </summary>
    public static bool TentarCriar(string? bruto, out CodigoDeTurma? codigo, out string? erro)
    {
        codigo = null;
        erro = null;

        var limpo = new string((bruto ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (limpo.Length == 0) { erro = "Informe o código da turma."; return false; }
        if (limpo.Length != Tamanho) { erro = $"O código tem {Tamanho} caracteres."; return false; }
        if (limpo.Any(c => !Alfabeto.Contains(c)))
        {
            // A MENSAGEM DIZ O QUE FAZER. "Código inválido" faria a pessoa tentar o mesmo de novo; dizer
            // que 0 e O não existem aqui resolve o caso real, que é ter lido errado do quadro.
            erro = "Esse código tem uma letra que não usamos (0, O, 1, I e L ficam de fora para não confundir).";
            return false;
        }

        codigo = new CodigoDeTurma(limpo);
        return true;
    }

    public override string ToString() => Valor;
}

/// <summary>
/// Uma turma: alguém que acompanha, e os alunos que aceitaram ser acompanhados.
///
/// A TURMA INVERTE O CONVITE, E SÓ ISSO. Sem ela, um professor com trinta alunos depende de trinta
/// pessoas lembrarem de digitar o apelido dele — e a chance de os trinta fazerem isso é zero. Com ela,
/// ele cria uma vez, dita o código, e cada aluno entra.
///
/// O QUE NÃO MUDA É QUEM CONCEDE. Entrar na turma é um ato do ALUNO, e o que ele grava é um
/// <see cref="Acompanhamento"/> normal, do vault dele para o professor — o mesmo que ele criaria à mão
/// no painel, e que ele pode desfazer no mesmo lugar. A turma não tem poder próprio sobre nada: ela é
/// um jeito de organizar convites, não uma autoridade acima deles. Se um dia a tabela de turmas sumir,
/// ninguém perde acesso nem ganha.
/// </summary>
public sealed record Turma
{
    public CodigoDeTurma Codigo { get; }
    public ApelidoDoUsuario Dono { get; }
    public string Nome { get; }

    private Turma(CodigoDeTurma codigo, ApelidoDoUsuario dono, string nome)
    {
        Codigo = codigo;
        Dono = dono;
        Nome = nome;
    }

    public const int TamanhoMaximoDoNome = 80;

    public static Turma? TentarCriar(CodigoDeTurma? codigo, ApelidoDoUsuario? dono, string? nome)
    {
        if (codigo is null || dono is null) return null;

        var limpo = (nome ?? "").Trim();
        if (limpo.Length == 0 || limpo.Length > TamanhoMaximoDoNome) return null;

        return new Turma(codigo, dono, limpo);
    }
}
