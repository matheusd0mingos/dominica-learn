using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Compartilhamento;

/// <summary>O que o convidado pode fazer na pasta.</summary>
public enum PapelNoCompartilhamento
{
    /// <summary>Vê e leva embora; nunca escreve.</summary>
    Leitor,

    /// <summary>Escreve junto — é o grupo montando o material.</summary>
    Editor,
}

/// <summary>
/// Uma concessão: <c>{dono}</c> deu a <c>{convidado}</c> acesso à pasta <c>{Pasta}</c> do vault dele.
///
/// A PASTA É UM SEGMENTO DE PRIMEIRO NÍVEL — uma matéria — e não um caminho qualquer. Permitir
/// "Direito/Licitações" como unidade de compartilhamento pareceria mais flexível e criaria concessões
/// aninhadas: duas regras podendo valer para o mesmo arquivo, uma dizendo "lê" e outra "escreve". Regra
/// de acesso com sobreposição é onde nasce o vazamento que ninguém consegue depurar.
///
/// ISTO NÃO É CONHECIMENTO DO USUÁRIO, e por isso não mora nem no índice nem no frontmatter da nota:
///
///   • no índice, um "reindexar do zero" — que a documentação manda fazer quando algo está estranho —
///     revogaria o acesso de todo mundo, ou o recriaria errado;
///
///   • no frontmatter, quem recebesse uma cópia do .md receberia junto a lista de quem mais tem acesso,
///     e editar o arquivo num editor de texto concederia acesso a si mesmo.
///
/// Concessão é autorização. Mora com a identidade.
/// </summary>
public sealed record Concessao
{
    public ApelidoDoUsuario Dono { get; }
    public string Pasta { get; }
    public ApelidoDoUsuario Convidado { get; }
    public PapelNoCompartilhamento Papel { get; }

    private Concessao(ApelidoDoUsuario dono, string pasta, ApelidoDoUsuario convidado, PapelNoCompartilhamento papel)
    {
        Dono = dono;
        Pasta = pasta;
        Convidado = convidado;
        Papel = papel;
    }

    /// <summary>
    /// Devolve null quando a concessão não faz sentido. Falhar aqui é o contrário de gravar uma regra de
    /// acesso malformada que depois vale mais ou menos.
    /// </summary>
    public static Concessao? TentarCriar(
        ApelidoDoUsuario? dono, string? pasta, ApelidoDoUsuario? convidado, PapelNoCompartilhamento papel)
    {
        if (dono is null || convidado is null) return null;

        // Compartilhar consigo mesmo não é um caso a tratar mais adiante — é uma concessão que existiria
        // sem significado, e que faria a lista de "quem tem acesso" mentir.
        if (dono == convidado) return null;

        var limpa = (pasta ?? string.Empty).Trim().Trim('/');
        if (limpa.Length == 0) return null;

        // Só primeiro nível. Ver o comentário da classe: aninhar concessões cria sobreposição de regras.
        if (limpa.Contains('/')) return null;

        // "." e ".." nunca chegariam a um caminho por CaminhoNota, mas uma concessão é construída de
        // texto vindo de fora e é a primeira porta — conferir aqui custa nada.
        if (limpa is "." or "..") return null;

        return new Concessao(dono, limpa, convidado, papel);
    }

    /// <summary>Este caminho do vault do dono está dentro da pasta concedida?</summary>
    public bool Alcanca(CaminhoNota caminho)
    {
        ArgumentNullException.ThrowIfNull(caminho);

        // COMPARAÇÃO POR SEGMENTO, e não por prefixo de texto. "Contabilidade Avançada 2/x.md" começa com
        // "Contabilidade Avançada" e NÃO está dentro dela. O erro de prefixo é o clássico aqui, e ele não
        // dá erro: concede acesso a mais do que devia, em silêncio.
        //
        // Segmentos são só os da PASTA (vazio na raiz), então o primeiro deles é a matéria. Uma nota na
        // raiz do vault não está dentro de pasta nenhuma e nunca é alcançada.
        var segmentos = caminho.Segmentos;
        return segmentos.Count > 0
            && string.Equals(segmentos[0], Pasta, StringComparison.Ordinal);
    }
}
