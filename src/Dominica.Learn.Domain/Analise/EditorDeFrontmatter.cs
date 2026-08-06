using System.Text;

namespace Dominica.Learn.Domain.Analise;

/// <summary>
/// Escreve campos no frontmatter de uma nota, preservando o resto do arquivo.
///
/// POR QUE ISTO EXISTE, E POR QUE É DOMÍNIO: favorito é conhecimento do usuário. Pela regra que rege o
/// projeto — *nada que exista só no índice pode ser conhecimento do usuário* — ele não pode morar numa
/// tabela do Postgres, senão some no primeiro "reindexar do zero" e é invisível no Obsidian Desktop.
/// Mora no frontmatter da própria nota, que é onde o Obsidian também guarda esse tipo de coisa.
///
/// A consequência é que marcar um favorito É UMA EDIÇÃO DE TEXTO MARKDOWN — regida por regras (o que
/// preservar, onde inserir, como não destruir o que já estava lá) — e isso é domínio, não infraestrutura.
///
/// O QUE ELE PRESERVA, DE PROPÓSITO:
///   • a ORDEM dos campos existentes. Reescrever o bloco inteiro a partir do <see cref="Frontmatter"/>
///     lido perderia tudo que o parser não entende (mapas aninhados, multi-linha) — e o frontmatter é do
///     usuário, não nosso. Só a linha do campo alvo é tocada;
///   • o CORPO da nota, byte a byte;
///   • a ausência de frontmatter: nota sem bloco ganha um bloco novo no topo, e só quando for preciso.
/// </summary>
public static class EditorDeFrontmatter
{
    /// <summary>
    /// Define (ou substitui) um campo escalar. <paramref name="valor"/> nulo REMOVE o campo — e remove o
    /// bloco inteiro se ele ficar vazio, para não deixar um "---\n---" órfão no topo de toda nota que já
    /// foi favorita um dia.
    /// </summary>
    public static string DefinirCampo(string conteudo, string chave, string? valor)
    {
        var texto = (conteudo ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var linhas = texto.Split('\n').ToList();
        var frontmatter = Frontmatter.Ler(linhas);
        var alvo = chave.Trim().ToLowerInvariant();

        if (!frontmatter.Existe)
        {
            if (valor is null) return conteudo ?? string.Empty;   // nada a remover
            var novo = new StringBuilder();
            novo.Append("---\n").Append(alvo).Append(": ").Append(Escapar(valor)).Append("\n---\n");
            // linha em branco entre o bloco e o corpo: é a convenção, e sem ela um "# Título" logo abaixo
            // de "---" fica visualmente colado no Obsidian.
            if (texto.Length > 0 && !texto.StartsWith('\n')) novo.Append('\n');
            novo.Append(texto);
            return novo.ToString();
        }

        var fim = frontmatter.LinhasOcupadas - 1;   // índice da linha do "---" de fechamento
        var indiceDoCampo = -1;
        for (var i = 1; i < fim; i++)
        {
            var doisPontos = linhas[i].IndexOf(':');
            if (doisPontos <= 0) continue;
            if (string.Equals(linhas[i][..doisPontos].Trim(), alvo, StringComparison.OrdinalIgnoreCase))
            {
                indiceDoCampo = i;
                break;
            }
        }

        if (valor is null)
        {
            if (indiceDoCampo < 0) return conteudo ?? string.Empty;
            // remove a linha e, se era lista em bloco, os itens dela
            var quantas = 1;
            for (var j = indiceDoCampo + 1; j < fim && linhas[j].TrimStart().StartsWith("- ", StringComparison.Ordinal); j++) quantas++;
            linhas.RemoveRange(indiceDoCampo, quantas);
            fim -= quantas;

            // bloco ficou sem nenhum campo: some com ele inteiro em vez de deixar "---\n---"
            var restou = false;
            for (var i = 1; i < fim; i++) if (linhas[i].Trim().Length > 0) { restou = true; break; }
            if (!restou)
            {
                linhas.RemoveRange(0, fim + 1);
                while (linhas.Count > 0 && linhas[0].Trim().Length == 0) linhas.RemoveAt(0);
            }
        }
        else if (indiceDoCampo >= 0)
        {
            linhas[indiceDoCampo] = $"{alvo}: {Escapar(valor)}";
        }
        else
        {
            linhas.Insert(fim, $"{alvo}: {Escapar(valor)}");
        }

        return string.Join('\n', linhas);
    }

    /// <summary>Marca ou desmarca a nota como favorita.</summary>
    public static string DefinirFavorito(string conteudo, bool favorito) =>
        // Remover o campo (em vez de gravar "false") mantém o frontmatter limpo: quem desfavoritou não
        // quer carregar a lembrança disso no topo do arquivo para sempre.
        DefinirCampo(conteudo, CampoFavorito, favorito ? "true" : null);

    public const string CampoFavorito = "favorito";

    /// <summary>Lê o favorito de uma análise já feita.</summary>
    public static bool EhFavorita(AnaliseDaNota analise) =>
        string.Equals(analise.Frontmatter.Texto(CampoFavorito), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Aspas só quando o valor tem algo que confundiria o leitor de YAML. Aspas em tudo funcionaria, mas
    /// deixaria o frontmatter feio de ler no Obsidian — e ele é para ser lido por humanos.
    /// </summary>
    private static string Escapar(string valor)
    {
        var precisa = valor.Length == 0
            || valor.IndexOfAny([':', '#', '[', ']', '{', '}', ',', '\n', '"', '\'']) >= 0
            || valor.TrimStart() != valor || valor.TrimEnd() != valor;
        return precisa ? "\"" + valor.Replace("\"", "\\\"") + "\"" : valor;
    }
}
