namespace Dominica.Learn.Domain.Anexos;

/// <summary>
/// O caminho de um ANEXO dentro do vault — imagem, PDF, áudio. Mesma raiz das notas, extensão livre.
///
/// POR QUE NÃO REAPROVEITAR <c>CaminhoNota</c>: aquele acrescenta ".md" ao que não tem, e é isso que o
/// torna correto para notas. Um "diagrama.png" passando por lá viraria "diagrama.png.md" — um arquivo que
/// o Obsidian trataria como nota e a busca indexaria como texto binário. As duas regras são parecidas de
/// olhar e opostas no ponto que importa; misturá-las é a economia que custa caro.
///
/// O anexo NÃO é indexado nem versionado: ele é conteúdo bruto, não conhecimento. O que fica no índice é
/// a nota que o referencia.
/// </summary>
public sealed record CaminhoDeAnexo
{
    private const char Separador = '/';

    /// <summary>
    /// Pasta padrão dos anexos. É uma pasta do VAULT, não um diretório da aplicação: quem abrir a mesma
    /// pasta no Obsidian tem de ver a imagem no lugar onde o `![[…]]` a procura.
    /// </summary>
    public const string PastaPadrao = "Anexos";

    public string Valor { get; }

    private CaminhoDeAnexo(string valor) => Valor = valor;

    /// <summary>Nome do arquivo com extensão.</summary>
    public string Arquivo
    {
        get
        {
            var barra = Valor.LastIndexOf(Separador);
            return barra < 0 ? Valor : Valor[(barra + 1)..];
        }
    }

    /// <summary>Extensão em minúsculas, com ponto ("" se não tiver).</summary>
    public string Extensao
    {
        get
        {
            var ponto = Arquivo.LastIndexOf('.');
            return ponto <= 0 ? string.Empty : Arquivo[ponto..].ToLowerInvariant();
        }
    }

    public bool EhImagem => TiposDeAnexo.EhImagem(Extensao);

    public static bool TentarCriar(string? bruto, out CaminhoDeAnexo? caminho, out string? erro)
    {
        caminho = null;
        erro = null;

        if (string.IsNullOrWhiteSpace(bruto)) { erro = "O caminho do anexo não pode ser vazio."; return false; }

        var normalizado = bruto.Replace('\\', Separador).Trim();
        while (normalizado.StartsWith(Separador)) normalizado = normalizado[1..];
        while (normalizado.Contains($"{Separador}{Separador}"))
            normalizado = normalizado.Replace($"{Separador}{Separador}", $"{Separador}");

        if (normalizado.Length == 0) { erro = "O caminho do anexo não pode ser vazio."; return false; }

        foreach (var s in normalizado.Split(Separador))
        {
            if (s.Length == 0) { erro = "O caminho tem um segmento vazio."; return false; }
            // ".." num caminho vindo de um `![[…]]` de nota é o vetor de leitura de arquivo do servidor.
            // A recusa mora aqui, no domínio, e o adaptador de disco confere de novo por caminho resolvido.
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

        if (normalizado.Length > 1 && normalizado[1] == ':') { erro = "O caminho não pode ser absoluto."; return false; }

        caminho = new CaminhoDeAnexo(normalizado);
        return true;
    }

    public static CaminhoDeAnexo De(string bruto) =>
        TentarCriar(bruto, out var c, out var erro) && c is not null
            ? c
            : throw new ArgumentException(erro ?? $"Caminho de anexo inválido: \"{bruto}\".", nameof(bruto));

    /// <summary>
    /// Limpa o nome que veio do navegador e o coloca na pasta de anexos.
    ///
    /// O nome de arquivo vindo de upload é entrada HOSTIL: pode trazer caminho ("../../etc/senha"), pode
    /// vir com o caminho completo do Windows ("C:\\Users\\…\\foto.png") e pode ter caractere que o disco
    /// do servidor aceita e o do usuário não. Aqui sobra só o nome, saneado.
    /// </summary>
    public static bool TentarDeUpload(string? nomeOriginal, out CaminhoDeAnexo? caminho, out string? erro)
    {
        caminho = null;
        erro = null;

        if (string.IsNullOrWhiteSpace(nomeOriginal)) { erro = "O arquivo não tem nome."; return false; }

        // fica só o último segmento: qualquer pasta que veio junto é descartada, não interpretada
        var bruto = nomeOriginal.Replace('\\', Separador);
        var soNome = bruto[(bruto.LastIndexOf(Separador) + 1)..].Trim();

        var limpo = new string(soNome.Select(c =>
            c is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '\0' or '#' or '[' or ']' ? '-' : c).ToArray())
            .TrimEnd(' ', '.');

        if (limpo.Length == 0) { erro = "O nome do arquivo ficou vazio depois da limpeza."; return false; }

        var extensao = ExtensaoDe(limpo);
        if (!TiposDeAnexo.EhPermitida(extensao))
        {
            // LISTA DE PERMISSÃO, não de bloqueio. Bloquear ".exe" e ".js" é uma corrida contra quem
            // inventa extensão nova; permitir o que o vault de fato usa não tem essa corrida.
            erro = $"Arquivos \"{(extensao.Length == 0 ? "sem extensão" : extensao)}\" não são aceitos como anexo.";
            return false;
        }

        return TentarCriar($"{PastaPadrao}{Separador}{limpo}", out caminho, out erro);
    }

    /// <summary>
    /// O mesmo anexo com um sufixo antes da extensão: "foto.png" → "foto-2.png".
    ///
    /// Serve para desviar de colisão. Sobrescrever em silêncio seria perder o anexo de uma nota antiga
    /// porque outra nota subiu um arquivo com o mesmo nome — e "captura de tela.png" é o nome mais
    /// repetido que existe.
    ///
    /// Quem sabe se colidiu é o armazém, que é assíncrono; por isso a decisão fica lá e aqui só a
    /// construção do nome. Um predicado síncrono nesta assinatura obrigaria o chamador a bloquear thread
    /// dentro de uma requisição.
    /// </summary>
    public CaminhoDeAnexo ComSufixo(int n)
    {
        var ponto = Valor.LastIndexOf('.');
        var (raiz, ext) = ponto <= Valor.LastIndexOf(Separador) ? (Valor, string.Empty) : (Valor[..ponto], Valor[ponto..]);
        return De($"{raiz}-{n}{ext}");
    }

    private static string ExtensaoDe(string arquivo)
    {
        var ponto = arquivo.LastIndexOf('.');
        return ponto <= 0 ? string.Empty : arquivo[ponto..].ToLowerInvariant();
    }

    private static readonly char[] CaracteresProibidos = ['<', '>', ':', '"', '|', '?', '*', '\0'];

    public bool Equals(CaminhoDeAnexo? outro) => outro is not null && string.Equals(Valor, outro.Valor, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Valor);
    public override string ToString() => Valor;
}

/// <summary>
/// O que pode ser anexado, e com que tipo servir.
///
/// O tipo MIME é decidido aqui e não pela extensão no momento de servir, porque servir com
/// "application/octet-stream" faria toda imagem virar download em vez de aparecer na nota — e servir com
/// um tipo adivinhado do conteúdo é como se serve HTML por engano.
/// </summary>
public static class TiposDeAnexo
{
    private static readonly Dictionary<string, string> Permitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        // SVG fica de FORA de propósito: SVG é XML que executa script, e a CSP com 'self' não ajuda num
        // arquivo servido pela própria origem. Uma imagem que roda código não é uma imagem.
        [".pdf"] = "application/pdf",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".ogg"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
    };

    private static readonly HashSet<string> Imagens =
        new([".png", ".jpg", ".jpeg", ".gif", ".webp"], StringComparer.OrdinalIgnoreCase);

    public static bool EhPermitida(string extensao) => Permitidos.ContainsKey(extensao);

    public static bool EhImagem(string extensao) => Imagens.Contains(extensao);

    public static string TipoDe(string extensao) =>
        Permitidos.TryGetValue(extensao, out var tipo) ? tipo : "application/octet-stream";

    public static IReadOnlyCollection<string> Extensoes => Permitidos.Keys;
}
