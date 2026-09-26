using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// A ordem da fila é o que faz a revisão funcionar ou virar teatro. Quem responde o quarto cartão
/// seguido da mesma nota está lembrando do que leu três linhas acima, não do conceito — e o esforço de
/// recuperar da memória é o mecanismo inteiro da repetição espaçada.
/// </summary>
public class OrdemDaFilaTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 7);

    private static Cartao Novo(string nota, int linha, DateOnly? vence = null) =>
        new(CaminhoNota.De(nota), $"{nota}#{linha}", "resposta", linha, 1,
            vence is null ? null : new Agendamento(vence.Value, 1, 250));

    private static string[] Notas(IEnumerable<Cartao> fila) => fila.Select(c => c.Nota.Nome).ToArray();

    [Fact]
    public void CartoesDaMesmaNotaNaoVemEmBloco()
    {
        // O CASO REAL. Três notas com três cartões cada, todos novos: sem intercalar, sai
        // A,A,A,B,B,B,C,C,C — que foi o que a matéria de Contabilidade Avançada produziu no teste.
        var fila = OrdemDaFila.Intercalar(
        [
            Novo("A.md", 1), Novo("A.md", 2), Novo("A.md", 3),
            Novo("B.md", 1), Novo("B.md", 2), Novo("B.md", 3),
            Novo("C.md", 1), Novo("C.md", 2), Novo("C.md", 3),
        ], Hoje);

        Assert.Equal(["A", "B", "C", "A", "B", "C", "A", "B", "C"], Notas(fila));
    }

    [Fact]
    public void OAtrasoContinuaMandandoAntesDaIntercalacao()
    {
        // Quem venceu há duas semanas vem antes de quem vence hoje, sempre. A intercalação só desempata
        // dentro de uma mesma data — inverter isso seria trocar o esquecimento de verdade por estética.
        var fila = OrdemDaFila.Intercalar(
        [
            Novo("B.md", 1),
            Novo("A.md", 1, new DateOnly(2026, 7, 24)),
            Novo("A.md", 2, new DateOnly(2026, 7, 24)),
        ], Hoje);

        Assert.Equal(["A", "A", "B"], Notas(fila));
    }

    [Fact]
    public void NotaComMaisCartoesTerminaSozinhaNoFim()
    {
        // Quando uma nota tem mais cartões que as outras, as rodadas finais só têm ela. É inevitável, e
        // o teste existe para registrar que o resultado é esse e não um erro.
        var fila = OrdemDaFila.Intercalar(
            [Novo("A.md", 1), Novo("A.md", 2), Novo("A.md", 3), Novo("B.md", 1)], Hoje);

        Assert.Equal(["A", "B", "A", "A"], Notas(fila));
    }

    [Fact]
    public void AOrdemEhDeterministica()
    {
        // A fila é recalculada a cada resposta, porque o serviço não guarda sessão. Ordem aleatória
        // mudaria a fila embaixo de quem está revisando, a cada cartão.
        Cartao[] Entrada() => [Novo("B.md", 2), Novo("A.md", 1), Novo("B.md", 1), Novo("A.md", 2)];

        Assert.Equal(
            Notas(OrdemDaFila.Intercalar(Entrada(), Hoje)),
            Notas(OrdemDaFila.Intercalar(Entrada().Reverse(), Hoje)));
    }

    [Fact]
    public void DentroDaNotaAOrdemEhADoTexto()
    {
        // O cartão que vem antes na nota vem antes na fila: numa nota bem escrita, isso é a ordem em que
        // as ideias se sustentam.
        var fila = OrdemDaFila.Intercalar([Novo("A.md", 9), Novo("A.md", 2), Novo("A.md", 5)], Hoje);

        Assert.Equal([2, 5, 9], fila.Select(c => c.Linha).ToArray());
    }

    [Fact]
    public void FilaVaziaNaoQuebra() => Assert.Empty(OrdemDaFila.Intercalar([], Hoje));
}
