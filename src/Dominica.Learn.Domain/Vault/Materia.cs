namespace Dominica.Learn.Domain.Vault;

/// <summary>
/// A MATÉRIA de uma nota — Direito Administrativo, Português, Raciocínio Lógico.
///
/// É a primeira pasta do caminho. "Direito/Administrativo/Licitações.md" é da matéria "Direito".
///
/// POR QUE A PASTA, E NÃO UMA ETIQUETA OU UM CAMPO NO FRONTMATTER:
///
/// A pasta já é a organização que o usuário escolheu e a que ele vê no explorador do Obsidian. Derivar a
/// matéria dela custa zero — nenhuma leitura de disco, nenhuma coluna nova, nada para manter em sincronia
/// — e nunca fica desatualizada, porque mover o arquivo JÁ é mudar a matéria.
///
/// Etiqueta seria a alternativa óbvia e é a errada: etiqueta é transversal por natureza (#revisar,
/// #lei-seca, #decorar), e usá-la como eixo principal faria uma nota pertencer a três matérias ao mesmo
/// tempo. A matéria é a divisão em que uma nota está em exatamente um lugar, e é justamente isso que a
/// torna útil para dividir a tela e o grafo.
///
/// O preço declarado: nota solta na raiz do vault não tem matéria. Isso não é defeito — é a lista do que
/// ainda não foi arquivado, e o produto a mostra como <see cref="Nenhuma"/> em vez de escondê-la.
/// </summary>
public sealed record Materia
{
    /// <summary>Nota na raiz do vault: ainda não pertence a nenhuma matéria.</summary>
    public static readonly Materia Nenhuma = new(string.Empty);

    /// <summary>Nome como está no disco, com a acentuação e a caixa originais. Vazio em <see cref="Nenhuma"/>.</summary>
    public string Nome { get; }

    private Materia(string nome) => Nome = nome;

    public bool Existe => Nome.Length > 0;

    /// <summary>O que aparece na tela — inclusive quando não há matéria.</summary>
    public string Rotulo => Existe ? Nome : "Sem matéria";

    /// <summary>
    /// A matéria de uma nota. Sempre derivada do caminho: uma regra só, no vault inteiro.
    ///
    /// Não há sobreposição por frontmatter de propósito. Um campo "materia:" que discordasse da pasta
    /// criaria duas verdades sobre onde a nota está — e a busca por pasta, que é prefixo de caminho,
    /// continuaria seguindo a pasta. Duas verdades em que uma delas é usada pela busca e a outra pela
    /// tela é o defeito que ninguém encontra.
    /// </summary>
    public static Materia De(CaminhoNota caminho)
    {
        var segmentos = caminho.Segmentos;
        return segmentos.Count == 0 ? Nenhuma : new Materia(segmentos[0]);
    }

    /// <summary>Para caminhos vindos de código e de teste.</summary>
    public static Materia De(string nome) =>
        string.IsNullOrWhiteSpace(nome) ? Nenhuma : new Materia(nome.Trim());

    /// <summary>
    /// Comparação ORDINAL, sensível a maiúsculas — ao contrário de <see cref="Analise.Etiqueta"/>.
    ///
    /// A diferença é deliberada e tem uma razão concreta: etiqueta é um rótulo digitado no meio do texto,
    /// onde a caixa é acidente de digitação. Matéria é uma PASTA, e no Linux "Direito" e "direito" são
    /// duas pastas de verdade. Juntá-las aqui faria a tela mostrar uma matéria só enquanto o filtro de
    /// busca — que é prefixo de caminho — encontraria as notas de apenas uma delas. Melhor mostrar as
    /// duas e deixar o erro de digitação visível.
    /// </summary>
    public bool Equals(Materia? outra) => outra is not null && string.Equals(Nome, outra.Nome, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Nome);
    public override string ToString() => Rotulo;
}
