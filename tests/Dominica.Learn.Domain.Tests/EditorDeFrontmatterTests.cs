using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Tests;

// Favorito mora no frontmatter da NOTA, não numa tabela — é a regra do projeto aplicada. Marcar um
// favorito é, portanto, editar Markdown, e o que não pode acontecer é essa edição estragar o arquivo.
public class EditorDeFrontmatterTests
{
    [Fact]
    public void Cria_o_bloco_quando_a_nota_nao_tem_frontmatter()
    {
        var r = EditorDeFrontmatter.DefinirFavorito("# Licitações\n\ntexto", true);
        Assert.StartsWith("---\nfavorito: true\n---\n", r);
        Assert.Contains("# Licitações", r);
    }

    [Fact]
    public void Acrescenta_ao_bloco_existente_sem_mexer_no_resto()
    {
        var r = EditorDeFrontmatter.DefinirFavorito("---\ntitle: Lei 14.133\ntags: [direito]\n---\n\ntexto", true);
        Assert.Contains("title: Lei 14.133", r);
        Assert.Contains("tags: [direito]", r);
        Assert.Contains("favorito: true", r);
        Assert.Contains("texto", r);
    }

    [Fact]
    public void Preserva_a_ordem_dos_campos_que_ja_existiam()
    {
        // Reescrever o bloco a partir do que o parser entendeu perderia tudo que ele não entende — e o
        // frontmatter é do usuário, não nosso.
        var r = EditorDeFrontmatter.DefinirFavorito("---\nzzz: ultimo\naaa: primeiro\n---\n\ntexto", true);
        Assert.True(r.IndexOf("zzz:", StringComparison.Ordinal) < r.IndexOf("aaa:", StringComparison.Ordinal));
    }

    [Fact]
    public void Substitui_em_vez_de_duplicar()
    {
        var r = EditorDeFrontmatter.DefinirCampo("---\nfavorito: true\n---\n\ntexto", "favorito", "false");
        Assert.Equal(1, r.Split("favorito:").Length - 1);   // o campo aparece UMA vez, não duas
        Assert.Contains("favorito: false", r);
    }

    [Fact]
    public void Desfavoritar_remove_o_campo_em_vez_de_gravar_false()
    {
        // Quem desfavoritou não quer carregar a lembrança disso no topo do arquivo para sempre.
        var r = EditorDeFrontmatter.DefinirFavorito("---\ntitle: X\nfavorito: true\n---\n\ntexto", false);
        Assert.DoesNotContain("favorito", r);
        Assert.Contains("title: X", r);
    }

    [Fact]
    public void Bloco_que_fica_vazio_e_removido_inteiro()
    {
        // Sem isto, toda nota que já foi favorita um dia carregaria um "---\n---" órfão no topo.
        var r = EditorDeFrontmatter.DefinirFavorito("---\nfavorito: true\n---\n\n# Título", false);
        Assert.StartsWith("# Título", r);
        Assert.DoesNotContain("---", r);
    }

    [Fact]
    public void Corpo_da_nota_e_preservado()
    {
        const string corpo = "# Título\n\nparágrafo com [[ligação]] e #etiqueta\n\n```\ncódigo ---\n```";
        var r = EditorDeFrontmatter.DefinirFavorito(corpo, true);
        Assert.EndsWith(corpo, r);
    }

    [Fact]
    public void Le_o_favorito_de_volta()
    {
        var marcada = EditorDeFrontmatter.DefinirFavorito("texto", true);
        Assert.True(EditorDeFrontmatter.EhFavorita(AnalisadorDeNota.Analisar(marcada, "N")));
        Assert.False(EditorDeFrontmatter.EhFavorita(AnalisadorDeNota.Analisar("texto", "N")));
    }

    [Fact]
    public void Ida_e_volta_devolve_o_conteudo_original()
    {
        // Marcar e desmarcar não pode deixar cicatriz no arquivo.
        const string original = "---\ntitle: X\n---\n\ncorpo";
        var ida = EditorDeFrontmatter.DefinirFavorito(original, true);
        Assert.Equal(original, EditorDeFrontmatter.DefinirFavorito(ida, false));
    }

    [Fact]
    public void Valor_com_dois_pontos_sai_entre_aspas()
    {
        var r = EditorDeFrontmatter.DefinirCampo("texto", "titulo", "Lei: a nova");
        Assert.Contains("titulo: \"Lei: a nova\"", r);
    }

    [Fact]
    public void Remover_campo_inexistente_nao_muda_nada()
    {
        const string original = "---\ntitle: X\n---\n\ncorpo";
        Assert.Equal(original, EditorDeFrontmatter.DefinirCampo(original, "inexistente", null));
    }

    // —— MATÉRIA DE REFERÊNCIA ————————————————————————————————————————————————————————
    [Fact]
    public void Referencia_entra_e_sai_sem_tocar_no_resto()
    {
        var original = "---\ntags: [materia]\n---\n# Trabalho\n\nMapa desta matéria.\n";

        var marcada = EditorDeFrontmatter.DefinirReferencia(original, true);
        Assert.Contains("referencia: true", marcada);
        Assert.Contains("tags: [materia]", marcada);
        Assert.Contains("Mapa desta matéria.", marcada);
        Assert.True(EditorDeFrontmatter.EhReferencia(AnalisadorDeNota.Analisar(marcada, "Trabalho")));

        // Desmarcar REMOVE o campo em vez de gravar "false": quem desmarcou não quer carregar a
        // lembrança disso no topo do arquivo para sempre. Mesmo critério do favorito.
        var desmarcada = EditorDeFrontmatter.DefinirReferencia(marcada, false);
        Assert.DoesNotContain("referencia", desmarcada);
        Assert.Contains("tags: [materia]", desmarcada);
        Assert.False(EditorDeFrontmatter.EhReferencia(AnalisadorDeNota.Analisar(desmarcada, "Trabalho")));
    }

    [Fact]
    public void Referencia_e_favorito_nao_se_atropelam()
    {
        // Os dois campos moram no mesmo bloco. Um que apagasse o outro seria descoberto tarde, e pelo
        // pior caminho: "meu favorito sumiu quando marquei referência".
        var nota = EditorDeFrontmatter.DefinirFavorito("# Trabalho\n", true);
        nota = EditorDeFrontmatter.DefinirReferencia(nota, true);

        var analise = AnalisadorDeNota.Analisar(nota, "Trabalho");
        Assert.True(EditorDeFrontmatter.EhFavorita(analise));
        Assert.True(EditorDeFrontmatter.EhReferencia(analise));

        var semReferencia = AnalisadorDeNota.Analisar(EditorDeFrontmatter.DefinirReferencia(nota, false), "Trabalho");
        Assert.True(EditorDeFrontmatter.EhFavorita(semReferencia));
        Assert.False(EditorDeFrontmatter.EhReferencia(semReferencia));
    }

    [Fact]
    public void Nota_sem_frontmatter_ganha_bloco_ao_ser_marcada()
    {
        var marcada = EditorDeFrontmatter.DefinirReferencia("# Trabalho\n\nTexto.\n", true);
        Assert.StartsWith("---\nreferencia: true\n---\n", marcada.Replace("\r\n", "\n"));
        Assert.Contains("Texto.", marcada);
    }

}
