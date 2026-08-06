using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Reconciliacao;

/// <summary>
/// O que se sabe de uma nota sem abrir o conteúdo: onde está, quando mudou, e a impressão do que tem
/// dentro. É a moeda da reconciliação — de um lado o que o DISCO diz, do outro o que o ÍNDICE lembra.
/// </summary>
public sealed record EstadoDaNota(CaminhoNota Caminho, DateTimeOffset ModificadoEm, ImpressaoDigital Impressao);

/// <summary>O que aconteceu com uma nota entre o índice e o disco.</summary>
public enum TipoDeDivergencia
{
    /// <summary>Existe no disco e não no índice.</summary>
    Criada,
    /// <summary>Existe nos dois, com conteúdo diferente.</summary>
    Alterada,
    /// <summary>Existe no índice e não no disco.</summary>
    Removida,
    /// <summary>Sumiu de um caminho e apareceu em outro, com o MESMO conteúdo.</summary>
    Renomeada,
}

/// <summary>
/// Uma diferença entre o disco e o índice. <see cref="CaminhoAnterior"/> só é preenchido em renomeação.
/// </summary>
public sealed record Divergencia(TipoDeDivergencia Tipo, CaminhoNota Caminho, CaminhoNota? CaminhoAnterior = null)
{
    public override string ToString() =>
        Tipo == TipoDeDivergencia.Renomeada ? $"{Tipo}: {CaminhoAnterior} → {Caminho}" : $"{Tipo}: {Caminho}";
}

/// <summary>Resultado completo de uma comparação, já separado por tipo para quem for aplicar.</summary>
public sealed record ResultadoDaReconciliacao(IReadOnlyList<Divergencia> Divergencias)
{
    public static readonly ResultadoDaReconciliacao Nenhuma = new(Array.Empty<Divergencia>());

    public bool EmDia => Divergencias.Count == 0;
    public IEnumerable<Divergencia> Criadas => Divergencias.Where(d => d.Tipo == TipoDeDivergencia.Criada);
    public IEnumerable<Divergencia> Alteradas => Divergencias.Where(d => d.Tipo == TipoDeDivergencia.Alterada);
    public IEnumerable<Divergencia> Removidas => Divergencias.Where(d => d.Tipo == TipoDeDivergencia.Removida);
    public IEnumerable<Divergencia> Renomeadas => Divergencias.Where(d => d.Tipo == TipoDeDivergencia.Renomeada);
}
