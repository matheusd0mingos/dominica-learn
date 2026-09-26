using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// Desfazer a última resposta.
///
/// É a única ação irreversível do ciclo diário — e ela acontece onde o erro é mais provável: no celular,
/// com o polegar, em sequência. Tocar "Fácil" num cartão que se errou tira o cartão da frente por
/// semanas, e o estrago não aparece hoje: aparece na prova.
///
/// A propriedade que estes testes protegem é uma só: DESFAZER TEM DE DEVOLVER O ARQUIVO AO BYTE ANTERIOR.
/// Um desfazer que "reagenda para amanhã" não desfaz — inventa um terceiro estado que nunca existiu.
/// </summary>
public class DesfazerRevisaoTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 7);
    private static CaminhoNota N => CaminhoNota.De("Direito/Licitações.md");

    private static Cartao Unico(string conteudo) =>
        Assert.Single(AnalisadorDeCartoes.Analisar(N, conteudo));

    /// <summary>Responde como o serviço responde, para o teste exercitar o mesmo caminho.</summary>
    private static string Responder(string conteudo, Resposta resposta)
    {
        var c = Unico(conteudo);
        var novo = AgendadorSM2.Proximo(c.AgendamentoOu(Hoje), resposta, Hoje);
        return EhDeBloco(conteudo, c)
            ? EscritorDeAgendamento.AplicarEmBloco(conteudo, c, novo)
            : EscritorDeAgendamento.Aplicar(conteudo, c.Linha, novo);
    }

    private static bool EhDeBloco(string conteudo, Cartao c) => c.LinhasOcupadas > 1;

    private static string Desfazer(string conteudo, Agendamento? anterior)
    {
        var c = Unico(conteudo);
        if (anterior is null) return EscritorDeAgendamento.Remover(conteudo, c);
        return EhDeBloco(conteudo, c)
            ? EscritorDeAgendamento.AplicarEmBloco(conteudo, c, anterior)
            : EscritorDeAgendamento.Aplicar(conteudo, c.Linha, anterior);
    }

    // —— A VOLTA AO BYTE ANTERIOR ————————————————————————————————————————————————————

    [Fact]
    public void CartaoJaREVISADOVoltaIDENTICO()
    {
        var antes = "Prazo do pregão::10 dias <!--SR:!2026-08-14,6,250-->";
        var anterior = Unico(antes).Agendamento;

        var depois = Responder(antes, Resposta.Facil);
        Assert.NotEqual(antes, depois);                 // a resposta de fato mudou o arquivo

        Assert.Equal(antes, Desfazer(depois, anterior));
    }

    [Fact]
    public void CartaoINEDITOVoltaSEMMARCANENHUMA()
    {
        // O CASO QUE JUSTIFICA O Remover. Responder um cartão inédito ESCREVE a primeira marca. Desfazer
        // não pode escrever "volta amanhã": tem de devolvê-lo ao estado sem marca, ou ele deixa de ser
        // inédito para sempre — e o teto de cartões novos passa a contá-lo como já introduzido.
        var antes = "Prazo do pregão::10 dias";

        var depois = Responder(antes, Resposta.Bom);
        Assert.Contains("<!--SR:", depois, StringComparison.Ordinal);

        Assert.Equal(antes, Desfazer(depois, anterior: null));
        Assert.Null(Unico(Desfazer(depois, null)).Agendamento);
    }

    [Fact]
    public void MarcaEMLINHAPROPRIASomeAINHATODA()
    {
        // Deixar a linha vazia mudaria o espaçamento do arquivo — e a promessa do escritor é que nada
        // além da marca muda.
        var antes = "Prazo::10 dias\n\n## Outra coisa\n";
        var comMarca = "Prazo::10 dias\n<!--SR:!2026-08-14,6,250-->\n\n## Outra coisa\n";

        Assert.Equal(antes, EscritorDeAgendamento.Remover(comMarca, Unico(comMarca)));
    }

    [Fact]
    public void CartaoDEBLOCOTambemVolta()
    {
        var antes = "Qual o prazo?\n?\n10 dias úteis\n";
        var depois = Responder(antes, Resposta.Errei);
        Assert.NotEqual(antes, depois);

        Assert.Equal(antes, Desfazer(depois, anterior: null));
    }

    [Fact]
    public void ODESFAZERNaoMEXENoRestoDaNota()
    {
        var antes = "---\ntags: [direito]\n---\n\n# Título\n\nProsa que não pode mudar.\n\nPrazo::10 dias\n\nFim.\n";
        var depois = Responder(antes, Resposta.Facil);

        Assert.Equal(antes, Desfazer(depois, anterior: null));
    }

    [Fact]
    public void PreservaOFINALDELINHADoArquivo()
    {
        // Trocar \r\n por \n marcaria a nota inteira como alterada — uma revisão desfeita pareceria uma
        // reescrita completa para o vigia do vault.
        var antes = "Prazo::10 dias\r\n\r\nOutra linha\r\n";
        var depois = Responder(antes, Resposta.Bom);

        Assert.Equal(antes, Desfazer(depois, anterior: null));
    }

    // —— O CICLO TEM DE TERMINAR ——————————————————————————————————————————————————————

    [Fact]
    public void DesfazerOQueNAOTEMMARCADevolveAMESMAINSTANCIA()
    {
        // Mesma regra do Aplicar, e pelo mesmo motivo: gravar um texto "novo" idêntico acordaria o vigia
        // do vault sem nada ter mudado. A identidade da instância é o sinal, verificável, de que para.
        var conteudo = "Prazo::10 dias";

        Assert.Same(conteudo, EscritorDeAgendamento.Remover(conteudo, Unico(conteudo)));
    }

    [Fact]
    public void RemoverDuasVezesNaoMudaNada()
    {
        var comMarca = "Prazo::10 dias <!--SR:!2026-08-14,6,250-->";
        var uma = EscritorDeAgendamento.Remover(comMarca, Unico(comMarca));

        Assert.Same(uma, EscritorDeAgendamento.Remover(uma, Unico(uma)));
    }

    [Fact]
    public void ConteudoVAZIONaoQuebra()
    {
        var c = Unico("Prazo::10 dias");
        Assert.Equal(string.Empty, EscritorDeAgendamento.Remover(string.Empty, c));
    }

    // —— O QUE O DESFAZER RESTAURA DE VERDADE ————————————————————————————————————————

    [Fact]
    public void RestauraOINTERVALOEAFACILIDADE_naoSoADATA()
    {
        // "Fácil" mexe em três números de uma vez: vencimento, intervalo e facilidade. Um desfazer que
        // só corrigisse a data deixaria o cartão com a facilidade inflada — e o erro seguiria crescendo
        // em cada revisão futura, invisível.
        var antes = "Prazo::10 dias <!--SR:!2026-08-14,6,250-->";
        var anterior = Unico(antes).Agendamento!;

        var voltou = Unico(Desfazer(Responder(antes, Resposta.Facil), anterior)).Agendamento!;

        Assert.Equal(anterior.Vence, voltou.Vence);
        Assert.Equal(anterior.IntervaloEmDias, voltou.IntervaloEmDias);
        Assert.Equal(anterior.Facilidade, voltou.Facilidade);
    }
}
