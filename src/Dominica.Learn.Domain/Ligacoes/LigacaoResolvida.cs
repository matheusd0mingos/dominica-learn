using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Ligacoes;

/// <summary>
/// Uma ligação depois de resolvida: sabe de onde sai e, quando existe, para onde chega.
///
/// <see cref="Destino"/> nulo significa LIGAÇÃO QUEBRADA — e isso não é erro. Escrever "[[Princípio da
/// Legalidade]]" antes de existir a nota é o jeito certo de usar a ferramenta: o link vira uma lista de
/// tarefas do que ainda falta estudar. O sistema tem de mostrar essas ligações, não escondê-las.
/// </summary>
public sealed record LigacaoResolvida
{
    public required CaminhoNota Origem { get; init; }
    public required string Alvo { get; init; }
    public CaminhoNota? Destino { get; init; }
    public string? Secao { get; init; }
    public string? Rotulo { get; init; }
    public FormaDaLigacao Forma { get; init; }
    public int Posicao { get; init; }

    public bool Quebrada => Destino is null;

    /// <summary>Ligação para dentro da própria nota ("[[#Seção]]") — não conta como backlink.</summary>
    public bool EhInterna => Alvo.Length == 0 && Secao is not null;
}
