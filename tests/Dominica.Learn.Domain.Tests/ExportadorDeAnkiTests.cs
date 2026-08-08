using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O arquivo de exportação é lido pelo IMPORTADOR DO ANKI, não por gente: um tab perdido dentro de um
/// campo desloca todas as colunas e o erro só aparece do outro lado, num app que não é nosso.
/// </summary>
public class ExportadorDeAnkiTests
{
    private static Cartao C(string nota, string frente, string verso, bool suspenso = false) =>
        new(CaminhoNota.De(nota), frente, verso, 1, 1, null, suspenso);

    [Fact]
    public void Os_cabecalhos_do_importador_vem_primeiro()
    {
        var texto = ExportadorDeAnki.Exportar([C("Direito/A.md", "p", "r")]);
        Assert.StartsWith("#separator:tab\n#html:true\n#tags column:3\n", texto);
    }

    [Fact]
    public void Cada_cartao_e_uma_linha_com_tres_colunas()
    {
        var texto = ExportadorDeAnki.Exportar([C("Direito/A.md", "pergunta", "resposta")]);
        var linha = texto.Split('\n')[3];
        Assert.Equal("pergunta\tresposta\tdominica Direito", linha);
    }

    [Fact]
    public void Tab_e_quebra_de_linha_no_conteudo_nao_quebram_o_formato()
    {
        // O verso de bloco tem várias linhas; um tab digitado no meio deslocaria as colunas TODAS.
        var texto = ExportadorDeAnki.Exportar([C("A.md", "com\ttab", "linha um\nlinha dois")]);
        var linha = texto.Split('\n')[3];

        Assert.Equal(3, linha.Split('\t').Length);   // exatamente dois tabs: três colunas
        Assert.Contains("linha um<br>linha dois", linha);
        Assert.Contains("com tab", linha);
    }

    [Fact]
    public void Html_no_cartao_e_escapado()
    {
        // #html:true faz o Anki INTERPRETAR o campo — sem escapar, "<b>" colado numa nota viraria
        // formatação e "<script>" viraria risco dentro do Anki.
        var texto = ExportadorDeAnki.Exportar([C("A.md", "<b>negrito?</b>", "r")]);
        Assert.Contains("&lt;b&gt;negrito?&lt;/b&gt;", texto);
    }

    [Fact]
    public void Materia_com_espaco_vira_tag_com_hifen()
    {
        // Tag do Anki não tem espaço: "Direito Administrativo" quebraria em duas tags sem isto.
        var texto = ExportadorDeAnki.Exportar([C("Direito Administrativo/A.md", "p", "r")]);
        Assert.Contains("dominica Direito-Administrativo", texto);
    }

    [Fact]
    public void Suspenso_vai_junto_com_a_marca()
    {
        var texto = ExportadorDeAnki.Exportar([C("A.md", "p", "r", suspenso: true)]);
        Assert.Contains("suspenso", texto.Split('\n')[3]);
    }

    [Fact]
    public void Nota_na_raiz_leva_so_a_tag_dominica()
    {
        var texto = ExportadorDeAnki.Exportar([C("Solta.md", "p", "r")]);
        Assert.EndsWith("\tdominica", texto.Split('\n')[3]);
    }
}
