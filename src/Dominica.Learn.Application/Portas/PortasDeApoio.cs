using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>
/// O relógio, como porta.
///
/// Parece exagero até o primeiro teste de "revisão espaçada": para verificar que um cartão vence em 7
/// dias sem um relógio injetável, o teste precisaria dormir uma semana. Como a revisão espaçada é
/// funcionalidade declarada do roadmap, o relógio entra agora — custa uma interface hoje e evita
/// reescrever todos os casos de uso depois.
/// </summary>
public interface IRelogio
{
    DateTimeOffset Agora { get; }
}

/// <summary>Uma revisão arquivada de uma nota.</summary>
public sealed record Revisao(long Id, CaminhoNota Caminho, string Conteudo, DateTimeOffset Em, string? Autor);

/// <summary>
/// HISTÓRICO — as versões anteriores de cada nota.
///
/// É porta separada do índice de propósito. O índice é descartável; o histórico NÃO É: ele guarda texto
/// que não existe mais em lugar nenhum. Misturar os dois na mesma interface convidaria alguém a apagar o
/// histórico junto num "reindexar do zero", que é exatamente a operação que a arquitetura promete ser
/// segura. Interfaces separadas para garantias diferentes.
/// </summary>
public interface IHistoricoDeNotas
{
    Task ArquivarAsync(CaminhoNota caminho, string conteudo, string? autor, CancellationToken ct = default);
    Task<IReadOnlyList<Revisao>> ListarAsync(CaminhoNota caminho, int limite = 50, CancellationToken ct = default);
    Task<Revisao?> ObterAsync(long id, CancellationToken ct = default);
    /// <summary>Acompanha a nota quando ela muda de lugar — senão renomear apaga a memória dela.</summary>
    Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default);
}
