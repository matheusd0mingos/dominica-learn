using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// Renomear etiqueta reescreve TEXTO DO USUÁRIO em massa — o tipo de operação onde um erro não aparece
/// na hora e aparece em vinte notas depois. As regras de fronteira são as do analisador (o mesmo
/// código); o que se prova aqui é a reescrita em si.
/// </summary>
public class RenomeadorDeEtiquetaTests
{
    private static Etiqueta E(string v) => Etiqueta.TentarCriar(v)!;

    [Fact]
    public void Renomeia_no_texto()
    {
        var novo = RenomeadorDeEtiqueta.Renomear("estudar #pegadinh sempre", E("pegadinh"), E("pegadinha"));
        Assert.Equal("estudar #pegadinha sempre", novo);
    }

    [Fact]
    public void A_hierarquia_vai_junto()
    {
        var novo = RenomeadorDeEtiqueta.Renomear(
            "#direito e #direito/penal e #direito/civil", E("direito"), E("dir"));
        Assert.Equal("#dir e #dir/penal e #dir/civil", novo);
    }

    [Fact]
    public void Etiqueta_parecida_mas_diferente_fica_em_paz()
    {
        // "#direito" não pode arrastar "#direitos": prefixo de texto não é prefixo de hierarquia.
        var conteudo = "#direitos humanos e #direito";
        var novo = RenomeadorDeEtiqueta.Renomear(conteudo, E("direito"), E("dir"));
        Assert.Equal("#direitos humanos e #dir", novo);
    }

    [Fact]
    public void Renomeia_no_frontmatter_e_deduplica_na_mesclagem()
    {
        // A nota tinha as DUAS ("pegadinh" e "pegadinha"): mesclar não pode deixar a lista com repetida —
        // a sujeira ficaria visível no Obsidian, que lê o mesmo arquivo.
        var conteudo = "---\ntags: [pegadinh, pegadinha]\n---\n\ncorpo";
        var novo = RenomeadorDeEtiqueta.Renomear(conteudo, E("pegadinh"), E("pegadinha"));

        // "pegadinha" aparece UMA vez no arquivo — a lista foi reescrita sem duplicar.
        Assert.Equal(1, novo.Split("pegadinha").Length - 1);
        Assert.Contains("corpo", novo);
    }

    [Fact]
    public void Dentro_de_bloco_de_codigo_nada_muda()
    {
        var conteudo = "```\n#pegadinh no código\n```\n\n#pegadinh de verdade";
        var novo = RenomeadorDeEtiqueta.Renomear(conteudo, E("pegadinh"), E("pegadinha"));

        Assert.Contains("#pegadinh no código", novo);
        Assert.Contains("#pegadinha de verdade", novo);
    }

    [Fact]
    public void Codigo_em_linha_tambem_fica_em_paz()
    {
        var conteudo = "use `#pegadinh` assim — e marque #pegadinh";
        var novo = RenomeadorDeEtiqueta.Renomear(conteudo, E("pegadinh"), E("pegadinha"));

        Assert.Contains("`#pegadinh`", novo);
        Assert.EndsWith("#pegadinha", novo);
    }

    [Fact]
    public void Cabecalho_nao_e_etiqueta_e_nao_e_tocado()
    {
        var conteudo = "# pegadinh\n\ntexto";
        var novo = RenomeadorDeEtiqueta.Renomear(conteudo, E("pegadinh"), E("pegadinha"));
        Assert.Same(conteudo, novo);
    }

    [Fact]
    public void Sem_nada_a_mudar_devolve_a_MESMA_instancia()
    {
        // É como o chamador sabe que não precisa gravar — e o histórico não ganha revisão idêntica.
        var conteudo = "nota sobre #outra coisa";
        Assert.Same(conteudo, RenomeadorDeEtiqueta.Renomear(conteudo, E("pegadinh"), E("pegadinha")));
    }

    [Fact]
    public void A_pontuacao_depois_da_etiqueta_sobrevive()
    {
        // O analisador apara "." e ":" do fim; a reescrita precisa trocar SÓ a etiqueta.
        var novo = RenomeadorDeEtiqueta.Renomear("cuidado com #pegadinh.", E("pegadinh"), E("pegadinha"));
        Assert.Equal("cuidado com #pegadinha.", novo);
    }
}
