namespace Dominica.Learn.Domain.Vault;

/// <summary>
/// O caminho de uma nota DENTRO do vault, relativo à raiz, sempre com barra normal.
/// Ex.: "Direito/Administrativo/Licitações.md".
///
/// POR QUE A IDENTIDADE DA NOTA É O CAMINHO, E NÃO UM GUID: o vault é uma pasta de arquivos .md que tem
/// de abrir no Obsidian Desktop. Um GUID não existe no disco — identificar a nota por ele obrigaria a
/// manter um mapa GUID↔arquivo que quebra no instante em que alguém renomeia um arquivo fora do app,
/// que é exatamente o cenário que a promessa "abre no Obsidian" garante que vai acontecer. O caminho é
/// a identidade que o disco já reconhece.
///
/// O preço é conhecido e aceito: renomear uma nota MUDA a identidade dela. Quem renomeia é responsável
/// por reescrever os wikilinks que apontavam para o nome antigo (ver caso de uso de renomeação). É a
/// mesma escolha do Obsidian, e ela é o que mantém o vault legível por qualquer outra ferramenta.
/// </summary>
public sealed record CaminhoNota
{
    public const string Extensao = ".md";
    private const char Separador = '/';

    /// <summary>Caminho relativo normalizado, com barra normal e extensão .md. Nunca começa com barra.</summary>
    public string Valor { get; }

    private CaminhoNota(string valor) => Valor = valor;

    /// <summary>Nome do arquivo sem a extensão — é o que o Obsidian usa como nome da nota.</summary>
    public string Nome
    {
        get
        {
            var barra = Valor.LastIndexOf(Separador);
            var arquivo = barra < 0 ? Valor : Valor[(barra + 1)..];
            return arquivo[..^Extensao.Length];
        }
    }

    /// <summary>Pasta que contém a nota ("" na raiz do vault).</summary>
    public string Pasta
    {
        get
        {
            var barra = Valor.LastIndexOf(Separador);
            return barra < 0 ? string.Empty : Valor[..barra];
        }
    }

    /// <summary>Segmentos da pasta, de fora para dentro. Vazio na raiz.</summary>
    public IReadOnlyList<string> Segmentos =>
        Pasta.Length == 0 ? Array.Empty<string>() : Pasta.Split(Separador);

    /// <summary>
    /// Cria um caminho a partir de texto livre, normalizando separadores e recusando o que sairia do vault.
    /// Devolve false em vez de lançar: caminho inválido é entrada do usuário, não defeito de programa.
    /// </summary>
    public static bool TentarCriar(string? bruto, out CaminhoNota? caminho, out string? erro)
    {
        caminho = null;
        erro = null;

        if (string.IsNullOrWhiteSpace(bruto)) { erro = "O caminho da nota não pode ser vazio."; return false; }

        // barra invertida do Windows vira barra normal: o vault tem de ser o mesmo em qualquer sistema
        var normalizado = bruto.Replace('\\', Separador).Trim();
        while (normalizado.StartsWith(Separador)) normalizado = normalizado[1..];

        // colapsa "//" repetido, que aparece em concatenação descuidada e viraria segmento vazio
        while (normalizado.Contains($"{Separador}{Separador}"))
            normalizado = normalizado.Replace($"{Separador}{Separador}", $"{Separador}");

        if (normalizado.Length == 0) { erro = "O caminho da nota não pode ser vazio."; return false; }

        // ESCAPE DO VAULT: ".." é a forma clássica de ler /etc/passwd por um campo de texto. A recusa fica
        // aqui, no domínio, e não só no adaptador de disco — o domínio é quem sabe que um caminho de nota
        // é sempre relativo e sempre para dentro. O adaptador confere de novo, por profundidade.
        var segmentos = normalizado.Split(Separador);
        foreach (var s in segmentos)
        {
            if (s.Length == 0) { erro = "O caminho tem um segmento vazio."; return false; }
            if (s is "." or "..") { erro = "O caminho não pode conter \".\" nem \"..\"."; return false; }
            if (s.IndexOfAny(CaracteresProibidos) >= 0)
            {
                erro = $"O segmento \"{s}\" tem caractere inválido para nome de arquivo.";
                return false;
            }
            if (s.EndsWith(' ') || s.EndsWith('.'))
            {
                erro = $"O segmento \"{s}\" não pode terminar com espaço nem ponto.";
                return false;
            }
        }

        // caminho absoluto de Windows ("C:/…") escapa do vault tanto quanto "..": o ':' já é proibido acima,
        // mas a checagem explícita deixa a intenção legível para quem vier depois.
        if (normalizado.Length > 1 && normalizado[1] == ':') { erro = "O caminho não pode ser absoluto."; return false; }

        if (!normalizado.EndsWith(Extensao, StringComparison.OrdinalIgnoreCase))
            normalizado += Extensao;

        caminho = new CaminhoNota(normalizado);
        return true;
    }

    /// <summary>Versão que lança — só para caminhos vindos de código, nunca de entrada do usuário.</summary>
    public static CaminhoNota De(string bruto) =>
        TentarCriar(bruto, out var c, out var erro) && c is not null
            ? c
            : throw new ArgumentException(erro ?? $"Caminho de nota inválido: \"{bruto}\".", nameof(bruto));

    /// <summary>Novo caminho com o mesmo nome, em outra pasta.</summary>
    public CaminhoNota MoverPara(string novaPasta) =>
        De(novaPasta.Length == 0 ? $"{Nome}{Extensao}" : $"{novaPasta.TrimEnd(Separador)}{Separador}{Nome}{Extensao}");

    /// <summary>Novo caminho com outro nome, na mesma pasta.</summary>
    public CaminhoNota Renomear(string novoNome) =>
        De(Pasta.Length == 0 ? $"{novoNome}{Extensao}" : $"{Pasta}{Separador}{novoNome}{Extensao}");

    // Caracteres que não sobrevivem a algum dos sistemas de arquivos que o vault precisa atravessar.
    // A lista é a interseção conservadora (Windows é o mais restritivo) — o vault tem de poder ser
    // sincronizado para qualquer máquina sem virar um nome ilegal do outro lado.
    private static readonly char[] CaracteresProibidos = ['<', '>', ':', '"', '|', '?', '*', '\0'];

    /// <summary>
    /// Comparação é ORDINAL e sensível a maiúsculas. Linux distingue "Nota.md" de "nota.md" e o vault é
    /// a fonte da verdade — fingir que são a mesma nota faria o índice divergir do disco no primeiro
    /// arquivo que diferisse só na caixa.
    /// </summary>
    public bool Equals(CaminhoNota? outro) => outro is not null && string.Equals(Valor, outro.Valor, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Valor);
    public override string ToString() => Valor;
}
