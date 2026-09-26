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
    /// <summary>
    /// Agora, JÁ NO FUSO DO USUÁRIO — o deslocamento embutido é o do concurseiro, não o do servidor.
    ///
    /// A regra nasceu de um defeito conceitual achado na auditoria de deploy: o relógio devolvia UTC e
    /// o código fazia `.ToLocalTime()`, que num contêiner sem fuso É o próprio UTC. Para quem estuda em
    /// Brasília, às 21h o sistema virava o dia — a nota diária abria com a data de amanhã, o heatmap
    /// marcava o dia errado e os cartões de amanhã venciam à noite. Quem consome esta porta NUNCA deve
    /// chamar `.ToLocalTime()` no resultado: ele já vem no fuso certo, e reconverter estraga.
    /// </summary>
    DateTimeOffset Agora { get; }

    /// <summary>
    /// O fuso do produto — para converter TIMESTAMPS GUARDADOS (sessões, revisões) para o dia local de
    /// quem estudou. Padrão UTC nas implementações que não configuram: é o comportamento dos testes.
    /// </summary>
    TimeZoneInfo Fuso => TimeZoneInfo.Utc;
}

/// <summary>Uma revisão arquivada de uma nota.</summary>
public sealed record Revisao(long Id, CaminhoNota Caminho, string Conteudo, DateTimeOffset Em, string? Autor);

/// <summary>
/// Uma revisão SEM o conteúdo — para listagens. O conteúdo pode ter megabytes; uma lista que o
/// carregasse por linha transformaria "ver o que dá para recuperar" numa consulta pesada.
/// </summary>
public sealed record RevisaoResumida(long Id, CaminhoNota Caminho, DateTimeOffset Em);

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
    /// <summary>
    /// A revisão MAIS RECENTE de cada caminho que o histórico conhece. É o que permite listar as notas
    /// apagadas recuperáveis: quem está aqui e não está no índice foi apagado — e ainda tem volta.
    /// </summary>
    Task<IReadOnlyList<RevisaoResumida>> UltimaDeCadaAsync(CancellationToken ct = default);
}
