namespace Dominica.Learn.Domain.Vault;

/// <summary>
/// O nome de um vault — e, no disco, a segunda pasta do caminho: /dados/vault/matheus/estudo/…
///
/// POR QUE VAULTS SEPARADOS, E NÃO PASTAS DENTRO DE UM SÓ:
///
/// Pasta separa a organização; vault separa o CONHECIMENTO. A diferença aparece no wikilink: "[[...]]"
/// resolve por nome de arquivo no vault inteiro (ver <see cref="Ligacoes.ResolvedorDeWikilinks"/>), e com
/// notas de engenharia e de estudo no mesmo vault, um "[[Aterramento]]" escrito estudando pode apontar
/// para a nota de trabalho. Ninguém vê isso acontecer — vê um backlink estranho meses depois. Uma ligação
/// errada dentro do conhecimento é o defeito mais caro que este produto pode ter, porque o conhecimento é
/// o produto.
///
/// Some junto o resto: uma busca só, um grafo só, uma fila de revisão só, um export só.
///
/// CADA VAULT É UM VAULT DE OBSIDIAN DE VERDADE. Não é uma divisão que existe dentro da Dominica: é uma
/// pasta que se abre sozinha no Obsidian Desktop, com o grafo dela e mais nada. Se fosse só uma coluna
/// no banco, a separação sumiria no dia em que a pessoa levasse os arquivos embora — e levar os arquivos
/// embora é a promessa central daqui.
///
/// AS MESMAS REGRAS DO APELIDO, e pelos mesmos motivos: isto vira nome de diretório, aparece em URL e é
/// digitado por alguém cansado. Ver <see cref="ApelidoDoUsuario"/>, onde a razão está escrita por
/// inteiro. A diferença é que o vault TEM RÓTULO: a pasta é "direito-tributario" e a tela mostra o que a
/// pessoa escreveu. Apelido não precisa disso porque ele já é o nome de quem usa.
/// </summary>
public sealed record NomeDoVault
{
    public const int TamanhoMinimo = 2;
    public const int TamanhoMaximo = 32;

    /// <summary>
    /// O vault que existe antes de existir escolha. Toda conta nasce com ele, e é para dentro dele que a
    /// migração move o que já havia — ver <c>MigracaoParaVaults</c>.
    ///
    /// O NOME É "estudo" porque este produto é um app de estudo: quem nunca criar um segundo vault vai
    /// ver esta palavra na barra e no caminho, e ela tem de descrever o que está ali. "padrao" ou
    /// "principal" descreveriam o papel do vault no código, que não é assunto de quem usa.
    /// </summary>
    public static readonly NomeDoVault Padrao = new("estudo");

    /// <summary>O nome normalizado: minúsculas, sem acento, sem espaço. É a pasta.</summary>
    public string Valor { get; }

    private NomeDoVault(string valor) => Valor = valor;

    /// <summary>O que aparece na tela. Hoje é o próprio nome; ver a nota sobre rótulo no resumo.</summary>
    public string Rotulo => Valor;

    public static bool TentarCriar(string? bruto, out NomeDoVault? vault, out string? erro)
    {
        vault = null;
        erro = null;

        if (string.IsNullOrWhiteSpace(bruto)) { erro = "Dê um nome ao vault."; return false; }

        var texto = bruto.Trim().ToLowerInvariant();

        if (texto.Length < TamanhoMinimo) { erro = $"O nome precisa de pelo menos {TamanhoMinimo} caracteres."; return false; }
        if (texto.Length > TamanhoMaximo) { erro = $"O nome pode ter no máximo {TamanhoMaximo} caracteres."; return false; }
        if (!char.IsAsciiLetterLower(texto[0])) { erro = "O nome precisa começar com uma letra."; return false; }

        foreach (var c in texto)
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-') continue;
            erro = "Use apenas letras sem acento, números e hífen.";
            return false;
        }

        if (texto.EndsWith('-')) { erro = "O nome não pode terminar com hífen."; return false; }
        if (texto.Contains("--", StringComparison.Ordinal)) { erro = "O nome não pode ter dois hífens seguidos."; return false; }

        if (Reservados.Contains(texto))
        {
            erro = $"\"{texto}\" é um nome reservado. Escolha outro.";
            return false;
        }

        vault = new NomeDoVault(texto);
        return true;
    }

    public static NomeDoVault De(string bruto) =>
        TentarCriar(bruto, out var v, out var erro) && v is not null
            ? v
            : throw new ArgumentException(erro ?? $"Nome de vault inválido: \"{bruto}\".", nameof(bruto));

    /// <summary>
    /// Lê um nome vindo do DISCO ou do BANCO, onde ele já foi validado uma vez.
    ///
    /// Existe para que uma pasta criada à mão dentro do vault do usuário — coisa que vai acontecer, o
    /// produto inteiro convida a mexer nos arquivos — não derrube a listagem inteira por ter um caractere
    /// que a validação recusa hoje. Devolve nulo, e quem chama decide ignorar aquela pasta.
    /// </summary>
    public static NomeDoVault? Conhecido(string? bruto) =>
        TentarCriar(bruto, out var v, out _) ? v : null;

    private static readonly HashSet<string> Reservados = new(StringComparer.Ordinal)
    {
        // Nomes que já significam outra coisa DENTRO de um vault: dariam uma pasta de vault indistinguível
        // da pasta de anexos ou da de templates quando alguém olhasse o disco.
        "anexos", "templates", "template", "attachments",
        // Pastas de ferramenta que vivem na raiz de um vault de Obsidian.
        "obsidian", "trash", "git",
        // Rotas do próprio app: um vault chamado "notas" faria a URL do vault e a da tela colidirem.
        "notas", "grafo", "revisar", "painel", "etiquetas", "backup", "conta", "account", "admin",
        "administracao", "api", "saude", "health", "lib", "css", "js", "img", "static", "assets",
        "con", "prn", "aux", "nul",           // dispositivos do Windows: pasta impossível de criar lá
    };

    public bool Equals(NomeDoVault? outro) => outro is not null && string.Equals(Valor, outro.Valor, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Valor);
    public override string ToString() => Valor;
}
