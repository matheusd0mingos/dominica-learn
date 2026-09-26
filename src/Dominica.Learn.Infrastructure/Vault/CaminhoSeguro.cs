namespace Dominica.Learn.Infrastructure.Vault;

/// <summary>
/// Garante que um caminho fica DENTRO da raiz do vault — inclusive quando o desvio é um elo simbólico.
///
/// POR QUE ISTO NÃO É <c>Path.GetFullPath</c> SOZINHO, que é o que estava aqui antes:
///
/// <c>Path.GetFullPath</c> é puramente TEXTUAL. Ele resolve "a/../b" para "b" e normaliza separadores, e
/// é só isso. Um elo simbólico dentro do vault apontando para "/etc" produz um caminho absoluto que
/// COMEÇA com a raiz do vault e ainda assim lê fora dele — passa pela conferência sem esforço nenhum.
/// A comparação textual é uma boa primeira barreira e uma garantia falsa.
///
/// Aqui os elos são resolvidos segmento por segmento, subindo pelos ancestrais, porque o elo pode estar
/// em qualquer nível: em "vault/atalho/segredo.png" o arquivo é comum e a PASTA é que desvia.
///
/// O custo é algumas chamadas de "stat" por acesso — proporcional à profundidade do caminho, não ao
/// tamanho do vault. É o preço de a garantia ser verdadeira.
/// </summary>
internal static class CaminhoSeguro
{
    /// <summary>
    /// Resolve <paramref name="relativo"/> sob <paramref name="raizReal"/> e devolve o caminho absoluto,
    /// ou lança se ele escapar. <paramref name="raizReal"/> tem de vir de <see cref="Real"/> — comparar
    /// um caminho resolvido com uma raiz não resolvida recusaria acessos legítimos quando a própria raiz
    /// está atrás de um elo (é o caso de /tmp em alguns sistemas).
    /// </summary>
    public static string Combinar(string raizReal, string relativo, string descricao)
    {
        var combinado = Path.GetFullPath(Path.Combine(raizReal, relativo.Replace('/', Path.DirectorySeparatorChar)));

        // 1ª barreira, textual e barata: pega "..", caminho absoluto e o erro de programação comum.
        if (!DentroDe(raizReal, combinado))
            throw new UnauthorizedAccessException($"O caminho \"{descricao}\" sai da raiz do vault.");

        // 2ª barreira, real: pega o elo simbólico, que a primeira não vê.
        if (!DentroDe(raizReal, Real(combinado)))
            throw new UnauthorizedAccessException($"O caminho \"{descricao}\" aponta para fora da raiz do vault.");

        return combinado;
    }

    /// <summary>
    /// O caminho com todos os elos simbólicos resolvidos, em qualquer nível.
    ///
    /// Funciona para caminho que ainda não existe (é o caso de toda gravação nova): o que não existe não
    /// pode ser um elo, então o segmento é mantido e a recursão segue pelos ancestrais, que existem.
    /// </summary>
    public static string Real(string caminho)
    {
        var normalizado = Path.GetFullPath(caminho);
        var pai = Path.GetDirectoryName(normalizado);
        if (string.IsNullOrEmpty(pai)) return normalizado;   // raiz do sistema de arquivos

        var combinado = Path.Combine(Real(pai), Path.GetFileName(normalizado));

        FileSystemInfo info = Directory.Exists(combinado) ? new DirectoryInfo(combinado) : new FileInfo(combinado);
        if (!info.Exists) return combinado;

        try
        {
            // returnFinalTarget: um elo pode apontar para outro elo, e é o destino FINAL que importa.
            return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? combinado;
        }
        catch (IOException)
        {
            // Cadeia de elos circular ou funda demais. Não dá para provar que está dentro da raiz, então
            // não está: devolver o caminho não resolvido aqui seria transformar um erro em permissão.
            throw new UnauthorizedAccessException($"Não consegui resolver o caminho \"{caminho}\".");
        }
    }

    private static bool DentroDe(string raiz, string caminho)
    {
        // O separador entra na comparação para que "/vault-outro" não passe por começar com "/vault".
        var raizComSeparador = raiz.EndsWith(Path.DirectorySeparatorChar) ? raiz : raiz + Path.DirectorySeparatorChar;
        return caminho.StartsWith(raizComSeparador, StringComparison.Ordinal);
    }
}
