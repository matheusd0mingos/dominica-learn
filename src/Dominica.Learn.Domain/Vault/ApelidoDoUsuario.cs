namespace Dominica.Learn.Domain.Vault;

/// <summary>
/// O apelido de quem estuda — e, no disco, o nome da PASTA do vault dele: /dados/vault/matheus/…
///
/// POR QUE O APELIDO, E NÃO O ID DO USUÁRIO:
///
/// A promessa central do produto é que a pasta abre no Obsidian Desktop. Uma pasta chamada
/// "a3f1c8e2-…" cumpre a promessa e destrói a experiência dela: quem faz backup, quem sincroniza com
/// rsync ou quem só quer apontar o Obsidian para o próprio vault precisa reconhecer a pasta olhando.
///
/// POR QUE IMUTÁVEL:
///
/// O apelido É o caminho. Trocá-lo significa mover a árvore inteira no disco E reescrever o caminho de
/// toda linha do índice e do histórico, com o Obsidian de alguém possivelmente aberto no meio. Um
/// produto de uma pessoa não tem como garantir isso sem risco de perder nota. Então a regra é dita na
/// hora do cadastro e não muda: o e-mail e a senha mudam, o apelido não.
///
/// A validação é rígida de propósito. Este texto vai virar nome de diretório em Linux hoje e talvez em
/// Windows amanhã, vai aparecer em URL, e vai ser digitado por alguém às onze da noite. Minúsculas sem
/// acento, começando por letra, é o conjunto que sobrevive a tudo isso sem surpresa.
/// </summary>
public sealed record ApelidoDoUsuario
{
    public const int TamanhoMinimo = 3;
    public const int TamanhoMaximo = 32;

    /// <summary>O apelido normalizado: minúsculas, sem acento, sem espaço.</summary>
    public string Valor { get; }

    private ApelidoDoUsuario(string valor) => Valor = valor;

    public static bool TentarCriar(string? bruto, out ApelidoDoUsuario? apelido, out string? erro)
    {
        apelido = null;
        erro = null;

        if (string.IsNullOrWhiteSpace(bruto)) { erro = "Escolha um apelido."; return false; }

        var texto = bruto.Trim().ToLowerInvariant();

        if (texto.Length < TamanhoMinimo) { erro = $"O apelido precisa de pelo menos {TamanhoMinimo} caracteres."; return false; }
        if (texto.Length > TamanhoMaximo) { erro = $"O apelido pode ter no máximo {TamanhoMaximo} caracteres."; return false; }

        // Começar por letra evita o apelido "2024", que viraria uma pasta indistinguível de uma pasta de
        // ano, e evita o apelido "-x", que alguns comandos de terminal leem como opção.
        if (!char.IsAsciiLetterLower(texto[0])) { erro = "O apelido precisa começar com uma letra."; return false; }

        foreach (var c in texto)
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-') continue;
            // Acento é recusado, e não normalizado, porque "josé" e "jose" virariam a mesma pasta em
            // alguns sistemas e duas em outros — e a ambiguidade só apareceria no dia da migração.
            erro = "Use apenas letras sem acento, números e hífen.";
            return false;
        }

        if (texto.EndsWith('-')) { erro = "O apelido não pode terminar com hífen."; return false; }
        if (texto.Contains("--", StringComparison.Ordinal)) { erro = "O apelido não pode ter dois hífens seguidos."; return false; }

        if (Reservados.Contains(texto))
        {
            // Não é frescura: estes nomes viram rota, viram pasta de sistema, ou já significam outra coisa
            // dentro do vault. Um usuário chamado "anexos" teria a pasta dele confundida com a de anexos.
            erro = $"\"{texto}\" é um nome reservado. Escolha outro.";
            return false;
        }

        apelido = new ApelidoDoUsuario(texto);
        return true;
    }

    public static ApelidoDoUsuario De(string bruto) =>
        TentarCriar(bruto, out var a, out var erro) && a is not null
            ? a
            : throw new ArgumentException(erro ?? $"Apelido inválido: \"{bruto}\".", nameof(bruto));

    private static readonly HashSet<string> Reservados = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "root", "sistema", "system", "suporte", "support",
        "api", "conta", "account", "login", "logout", "notas", "grafo", "anexos", "saude", "health",
        "static", "assets", "lib", "css", "js", "img", "www", "public", "dominica", "learn",
        "con", "prn", "aux", "nul",           // nomes de dispositivo do Windows: pasta impossível de criar lá
        "templates", "template",              // já significa outra coisa dentro do vault
    };

    public bool Equals(ApelidoDoUsuario? outro) => outro is not null && string.Equals(Valor, outro.Valor, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Valor);
    public override string ToString() => Valor;
}
