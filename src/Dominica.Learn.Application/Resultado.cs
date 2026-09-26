namespace Dominica.Learn.Application;

/// <summary>Por que uma operação não deu certo — o suficiente para a interface decidir o que mostrar.</summary>
public enum MotivoDaFalha
{
    NaoEncontrada,
    JaExiste,
    /// <summary>O disco mudou desde que o cliente leu — outra aba, o Obsidian, um sincronizador.</summary>
    Conflito,
    Invalida,
}

/// <summary>
/// Resultado de um caso de uso: deu certo com um valor, ou falhou com um motivo e uma mensagem.
///
/// POR QUE NÃO EXCEÇÕES: "a nota não existe" e "o arquivo mudou por fora" não são situações
/// excepcionais neste produto — são o cotidiano de um vault que abre no Obsidian. Exceção para fluxo
/// esperado transforma o caminho normal em stack trace, esconde o caso de uso atrás de um `catch` e
/// custa caro num autosave que dispara a cada poucos segundos. Exceção fica para o que é defeito de
/// programa mesmo.
/// </summary>
public readonly record struct Resultado<T>
{
    public bool Ok { get; }
    public T? Valor { get; }
    public MotivoDaFalha? Motivo { get; }
    public string? Mensagem { get; }

    private Resultado(bool ok, T? valor, MotivoDaFalha? motivo, string? mensagem)
    {
        Ok = ok; Valor = valor; Motivo = motivo; Mensagem = mensagem;
    }

    public static Resultado<T> Sucesso(T valor) => new(true, valor, null, null);
    public static Resultado<T> Falha(MotivoDaFalha motivo, string mensagem) => new(false, default, motivo, mensagem);

    public static Resultado<T> NaoEncontrada(string oQue) => Falha(MotivoDaFalha.NaoEncontrada, $"{oQue} não foi encontrada.");
    public static Resultado<T> JaExiste(string oQue) => Falha(MotivoDaFalha.JaExiste, $"{oQue} já existe.");
    public static Resultado<T> Invalida(string porque) => Falha(MotivoDaFalha.Invalida, porque);
}

/// <summary>Versão sem valor, para comandos que só precisam dizer se deu certo.</summary>
public readonly record struct Resultado
{
    public bool Ok { get; }
    public MotivoDaFalha? Motivo { get; }
    public string? Mensagem { get; }

    private Resultado(bool ok, MotivoDaFalha? motivo, string? mensagem)
    {
        Ok = ok; Motivo = motivo; Mensagem = mensagem;
    }

    public static readonly Resultado Sucesso = new(true, null, null);
    public static Resultado Falha(MotivoDaFalha motivo, string mensagem) => new(false, motivo, mensagem);
    public static Resultado NaoEncontrada(string oQue) => Falha(MotivoDaFalha.NaoEncontrada, $"{oQue} não foi encontrada.");
    public static Resultado JaExiste(string oQue) => Falha(MotivoDaFalha.JaExiste, $"{oQue} já existe.");
    public static Resultado Invalida(string porque) => Falha(MotivoDaFalha.Invalida, porque);
}
