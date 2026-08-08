using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// PÔR E TIRAR ETIQUETA MEXE NO ARQUIVO DA PESSOA, e por isso estes testes olham o texto inteiro, e não
/// só "a etiqueta está lá". O corpo da nota é o produto: se um clique de etiqueta reescrever uma frase,
/// não há como saber quando isso começou.
/// </summary>
public class EtiquetasDaNotaTests
{
    private static Etiqueta E(string v) => Etiqueta.TentarCriar(v)!;

    private static AnaliseDaNota Analisar(string conteudo) =>
        AnalisadorDeNota.Analisar(conteudo, "Nota.md");

    private static (string, EtiquetasDaNota.Resultado) Marcar(string conteudo, string etiqueta) =>
        EtiquetasDaNota.Marcar(conteudo, E(etiqueta), Analisar(conteudo).Etiquetas);

    // —— PÔR ——————————————————————————————————————————————————————————————————————————

    [Fact]
    public void Nota_sem_frontmatter_ganha_um_bloco_e_o_corpo_fica_intacto()
    {
        var (conteudo, r) = Marcar("# Licitações\n\nLei 14.133 e o que ela revogou.\n", "direito");

        Assert.Equal(EtiquetasDaNota.Resultado.Mudou, r);
        Assert.StartsWith("---\ntags: [direito]\n---\n", conteudo);
        Assert.Contains("# Licitações\n\nLei 14.133 e o que ela revogou.\n", conteudo);
    }

    [Fact]
    public void Etiqueta_nova_entra_ao_lado_das_que_ja_estavam()
    {
        var (conteudo, _) = Marcar("---\ntags: [direito, penal]\n---\n\n# A\n", "decorar");

        Assert.Contains("tags: [direito, penal, decorar]", conteudo);
    }

    [Fact]
    public void O_estilo_de_lista_em_bloco_e_preservado()
    {
        // O frontmatter é lido por gente, no Obsidian. Trocar o estilo de quem já escreveu é mexer no
        // arquivo sem ser pedido — e faria o diff de um vault versionado explodir a cada clique.
        var (conteudo, _) = Marcar("---\ntags:\n  - direito\n  - penal\n---\n\n# A\n", "decorar");

        Assert.Contains("tags:\n  - direito\n  - penal\n  - decorar\n", conteudo);
        Assert.DoesNotContain("[", conteudo);
    }

    [Fact]
    public void Os_outros_campos_do_frontmatter_nao_sao_tocados()
    {
        // O parser é um subconjunto de YAML declarado: o que ele não entende tem de sobreviver intacto,
        // senão marcar uma etiqueta apaga o que o Obsidian ou um plugin escreveu.
        var original = "---\ntitle: Licitações\naliases: [Lei 14.133]\ncssclass: enxuto\n---\n\n# A\n";

        var (conteudo, _) = Marcar(original, "direito");

        Assert.Contains("title: Licitações", conteudo);
        Assert.Contains("aliases: [Lei 14.133]", conteudo);
        Assert.Contains("cssclass: enxuto", conteudo);
        Assert.Contains("tags: [direito]", conteudo);
    }

    [Fact]
    public void Por_a_mesma_etiqueta_duas_vezes_nao_escreve_de_novo()
    {
        var uma = "---\ntags: [direito]\n---\n\n# A\n";

        var (conteudo, r) = Marcar(uma, "direito");

        Assert.Equal(EtiquetasDaNota.Resultado.JaTinha, r);
        Assert.Equal(uma, conteudo);
    }

    [Fact]
    public void Etiqueta_que_ja_esta_no_TEXTO_nao_vira_linha_no_frontmatter()
    {
        // O CASO QUE DUPLICARIA A INFORMAÇÃO EM DOIS LUGARES DO MESMO ARQUIVO. No painel apareceria uma
        // vez só (a análise faz Distinct) e ninguém notaria — até alguém apagar uma das duas achando
        // que era sobra, e a etiqueta continuar valendo pela outra.
        var original = "# A\n\nIsso é #pegadinha clássica de prova.\n";

        var (conteudo, r) = Marcar(original, "pegadinha");

        Assert.Equal(EtiquetasDaNota.Resultado.JaTinha, r);
        Assert.Equal(original, conteudo);
    }

    [Fact]
    public void A_caixa_nao_cria_etiqueta_nova()
    {
        // "#Direito" e "#direito" são a mesma gaveta (ver Etiqueta.Equals). Escrever a segunda faria o
        // painel mostrar uma etiqueta com duas grafias e a contagem se partir em duas.
        var (conteudo, r) = Marcar("---\ntags: [Direito]\n---\n\n# A\n", "direito");

        Assert.Equal(EtiquetasDaNota.Resultado.JaTinha, r);
        Assert.Contains("tags: [Direito]", conteudo);
    }

    // —— TIRAR ————————————————————————————————————————————————————————————————————————

    [Fact]
    public void Tirar_deixa_as_outras_e_o_corpo_no_lugar()
    {
        var (conteudo, r) = EtiquetasDaNota.Desmarcar(
            "---\ntags: [direito, penal, decorar]\n---\n\n# A\n\nO corpo.\n", E("penal"));

        Assert.Equal(EtiquetasDaNota.Resultado.Mudou, r);
        Assert.Contains("tags: [direito, decorar]", conteudo);
        Assert.Contains("# A\n\nO corpo.\n", conteudo);
    }

    [Fact]
    public void Tirar_a_ultima_etiqueta_nao_deixa_um_campo_vazio_no_topo()
    {
        var (conteudo, _) = EtiquetasDaNota.Desmarcar("---\ntags: [direito]\n---\n\n# A\n", E("direito"));

        Assert.DoesNotContain("tags", conteudo);
        Assert.StartsWith("# A", conteudo);
    }

    [Fact]
    public void Tirar_a_ultima_de_um_frontmatter_com_outros_campos_preserva_o_bloco()
    {
        var (conteudo, _) = EtiquetasDaNota.Desmarcar(
            "---\ntitle: Licitações\ntags: [direito]\n---\n\n# A\n", E("direito"));

        Assert.Contains("title: Licitações", conteudo);
        Assert.DoesNotContain("tags", conteudo);
        Assert.StartsWith("---\n", conteudo);
    }

    [Fact]
    public void Etiqueta_escrita_no_texto_NAO_e_apagada_da_frase()
    {
        // A GARANTIA MAIS IMPORTANTE DESTA CLASSE. Apagar "#pegadinha" do meio de "isso é #pegadinha
        // clássica" reescreveria a frase de alguém por causa de um clique numa ficha. A tela mostra a
        // origem e desabilita o "tirar"; aqui, o texto volta idêntico.
        var original = "# A\n\nIsso é #pegadinha clássica de prova.\n";

        var (conteudo, r) = EtiquetasDaNota.Desmarcar(original, E("pegadinha"));

        Assert.Equal(EtiquetasDaNota.Resultado.NaoTinha, r);
        Assert.Equal(original, conteudo);
    }

    [Fact]
    public void Tirar_o_que_a_nota_nao_tem_nao_mexe_no_arquivo()
    {
        var original = "---\ntags: [direito]\n---\n\n# A\n";

        var (conteudo, r) = EtiquetasDaNota.Desmarcar(original, E("penal"));

        Assert.Equal(EtiquetasDaNota.Resultado.NaoTinha, r);
        Assert.Equal(original, conteudo);
    }

    [Fact]
    public void Tirar_limpa_as_duas_chaves_tags_e_tag()
    {
        // Estando nas duas, limpar só uma faria a ficha sumir e VOLTAR na releitura da nota — o clique
        // pareceria não ter funcionado.
        var (conteudo, r) = EtiquetasDaNota.Desmarcar(
            "---\ntags: [direito]\ntag: [direito, penal]\n---\n\n# A\n", E("direito"));

        Assert.Equal(EtiquetasDaNota.Resultado.Mudou, r);
        Assert.DoesNotContain("direito", conteudo);
        Assert.Contains("penal", conteudo);
    }

    // —— DE ONDE VEIO CADA UMA ————————————————————————————————————————————————————————

    [Fact]
    public void A_origem_de_cada_etiqueta_aparece_e_e_ela_que_diz_o_que_da_para_fazer()
    {
        var analise = Analisar("---\ntags: [direito]\n---\n\n# A\n\nIsso é #pegadinha clássica.\n");

        var todas = EtiquetasDaNota.De(analise);

        var doBloco = todas.Single(t => t.Etiqueta.Valor == "direito");
        var doTexto = todas.Single(t => t.Etiqueta.Valor == "pegadinha");

        Assert.True(doBloco.PodeTirar);
        Assert.False(doTexto.PodeTirar);

        // As do frontmatter vêm primeiro: são as que se pode tirar, e ordem é o que a mão espera.
        Assert.Equal("direito", todas[0].Etiqueta.Valor);
    }

    [Fact]
    public void Etiqueta_nas_duas_origens_aparece_UMA_vez_e_como_do_frontmatter()
    {
        // Aparecer duas vezes na tela seria mentira sobre o arquivo; aparecer como "do texto" tiraria da
        // pessoa o botão de remover que ela de fato tem.
        var analise = Analisar("---\ntags: [decorar]\n---\n\n# A\n\nIsto é #decorar puro.\n");

        var todas = EtiquetasDaNota.De(analise);

        Assert.Equal(OrigemDaEtiqueta.Frontmatter, Assert.Single(todas).Origem);
    }

    [Fact]
    public void Nota_sem_etiqueta_nenhuma_da_lista_vazia()
    {
        Assert.Empty(EtiquetasDaNota.De(Analisar("# A\n\nSó texto.\n")));
    }
}
