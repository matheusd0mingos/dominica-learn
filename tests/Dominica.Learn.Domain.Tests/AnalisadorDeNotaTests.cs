using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Tests;

// O analisador é a peça que mais vai ser lida por outro desenvolvedor daqui a cinco anos, porque é onde
// mora toda a interpretação do Markdown. Cada teste aqui documenta uma DECISÃO, não só um comportamento.
public class AnalisadorDeNotaTests
{
    private static AnaliseDaNota Analisar(string conteudo, string nome = "Nota") =>
        AnalisadorDeNota.Analisar(conteudo, nome);

    // —— TÍTULO ————————————————————————————————————————————————————————————————————————
    [Fact]
    public void Titulo_vem_do_frontmatter_quando_declarado()
    {
        var a = Analisar("---\ntitle: Lei 14.133\n---\n\n# Outro título\n");
        Assert.Equal("Lei 14.133", a.Titulo);
    }

    [Fact]
    public void Sem_frontmatter_o_titulo_e_o_primeiro_H1()
    {
        Assert.Equal("Licitações", Analisar("# Licitações\n\ntexto").Titulo);
    }

    [Fact]
    public void Sem_frontmatter_e_sem_H1_o_titulo_e_o_nome_do_arquivo()
    {
        Assert.Equal("Princípios", Analisar("só texto solto", "Princípios").Titulo);
    }

    // —— WIKILINKS ——————————————————————————————————————————————————————————————————————
    [Fact]
    public void Extrai_wikilink_simples()
    {
        var l = Assert.Single(Analisar("ver [[Licitações]] hoje").Ligacoes);
        Assert.Equal("Licitações", l.Alvo);
        Assert.Equal(FormaDaLigacao.Wikilink, l.Forma);
        Assert.Null(l.Rotulo);
        Assert.Null(l.Secao);
    }

    [Fact]
    public void Extrai_rotulo_e_secao()
    {
        var l = Assert.Single(Analisar("[[Direito/Licitações#Modalidades|as modalidades]]").Ligacoes);
        Assert.Equal("Direito/Licitações", l.Alvo);
        Assert.Equal("Modalidades", l.Secao);
        Assert.Equal("as modalidades", l.Rotulo);
        Assert.Equal("as modalidades", l.TextoExibido);
    }

    [Fact]
    public void Embed_e_distinguido_do_wikilink()
    {
        var l = Assert.Single(Analisar("![[Resumo]]").Ligacoes);
        Assert.Equal(FormaDaLigacao.Embed, l.Forma);
    }

    [Fact]
    public void Link_markdown_tambem_conta_como_ligacao()
    {
        // Um vault real tem os dois formatos. Ignorar o Markdown deixaria metade dos backlinks invisível.
        var l = Assert.Single(Analisar("veja [as regras](Direito/Licitações.md)").Ligacoes);
        Assert.Equal("Direito/Licitações.md", l.Alvo);
        Assert.Equal(FormaDaLigacao.Markdown, l.Forma);
        Assert.Equal("as regras", l.Rotulo);
    }

    [Fact]
    public void Link_externo_e_marcado_como_externo()
    {
        var a = Analisar("[norma](https://abnt.org.br) e [[Interna]]");
        Assert.Equal(2, a.Ligacoes.Count);
        Assert.Single(a.LigacoesInternas);
        Assert.Contains(a.Ligacoes, l => l.EhExterna);
    }

    [Fact]
    public void Ligacao_dentro_de_bloco_de_codigo_e_ignorada()
    {
        // Sem isto, toda nota sobre programação injeta lixo no grafo de conhecimento.
        var a = Analisar("antes\n```\n[[NãoÉLigação]]\n```\ndepois [[Real]]");
        var l = Assert.Single(a.Ligacoes);
        Assert.Equal("Real", l.Alvo);
    }

    [Fact]
    public void Ligacao_em_codigo_de_linha_e_ignorada()
    {
        var l = Assert.Single(Analisar("use `[[assim]]` para ligar a [[Nota]]").Ligacoes);
        Assert.Equal("Nota", l.Alvo);
    }

    [Fact]
    public void Cerca_de_til_nao_e_fechada_por_crase()
    {
        // ~~~ só fecha com ~~~. Tratar qualquer cerca como equivalente reabriria o bloco no meio.
        var a = Analisar("~~~\n[[Dentro]]\n```\n[[AindaDentro]]\n~~~\n[[Fora]]");
        var l = Assert.Single(a.Ligacoes);
        Assert.Equal("Fora", l.Alvo);
    }

    // —— ETIQUETAS ——————————————————————————————————————————————————————————————————————
    [Fact]
    public void Extrai_etiqueta_hierarquica()
    {
        var e = Assert.Single(Analisar("estudar #direito/administrativo hoje").Etiquetas);
        Assert.Equal("direito/administrativo", e.Valor);
        Assert.Equal(["direito", "administrativo"], e.Segmentos);
    }

    [Fact]
    public void Cabecalho_nao_e_etiqueta()
    {
        // "# Título" é cabeçalho; o espaço é o que separa os dois casos.
        var a = Analisar("# Direito Administrativo\n\ntexto");
        Assert.Empty(a.Etiquetas);
        Assert.Single(a.Cabecalhos);
    }

    [Fact]
    public void Etiqueta_puramente_numerica_e_recusada()
    {
        // "#2026" e "#1" apareceriam em qualquer nota com datas ou numeração de item, e encheriam o painel
        // de etiquetas de lixo. Já "#fff" É etiqueta válida — tem letras, e é assim no Obsidian também.
        var a = Analisar("ano #2026, item #1, e a etiqueta #fff");
        Assert.DoesNotContain(a.Etiquetas, e => e.Valor is "2026" or "1");
        Assert.Contains(a.Etiquetas, e => e.Valor == "fff");
    }

    [Fact]
    public void Ancora_de_url_nao_vira_etiqueta()
    {
        Assert.Empty(Analisar("veja https://x.com/pagina#secao").Etiquetas);
    }

    [Fact]
    public void Etiqueta_do_frontmatter_conta()
    {
        var a = Analisar("---\ntags: [direito, penal]\n---\n\ntexto");
        Assert.Equal(2, a.Etiquetas.Count);
        Assert.Contains(a.Etiquetas, e => e.Valor == "direito");
        Assert.Contains(a.Etiquetas, e => e.Valor == "penal");
    }

    [Fact]
    public void Etiqueta_do_frontmatter_em_lista_de_bloco_conta()
    {
        var a = Analisar("---\ntags:\n  - direito\n  - penal\n---\n\ntexto");
        Assert.Equal(2, a.Etiquetas.Count);
    }

    [Fact]
    public void Etiqueta_dentro_de_bloco_de_codigo_e_ignorada()
    {
        var a = Analisar("```c\n#include <stdio.h>\n```\n\n#direito");
        var e = Assert.Single(a.Etiquetas);
        Assert.Equal("direito", e.Valor);
    }

    [Fact]
    public void Pontuacao_final_nao_entra_na_etiqueta()
    {
        var e = Assert.Single(Analisar("estudei #direito.").Etiquetas);
        Assert.Equal("direito", e.Valor);
    }

    [Fact]
    public void Etiqueta_repetida_aparece_uma_vez_so()
    {
        Assert.Single(Analisar("#direito no começo e #Direito no fim").Etiquetas);
    }

    // —— TAREFAS ————————————————————————————————————————————————————————————————————————
    [Fact]
    public void Le_checklist_pendente_e_concluida()
    {
        var a = Analisar("- [ ] revisar\n- [x] ler\n* [ ] resolver questões");
        Assert.Equal(3, a.Tarefas.Count);
        Assert.Equal(2, a.Tarefas.Count(t => !t.Concluida));
        Assert.Equal("revisar", a.Tarefas[0].Texto);
    }

    [Fact]
    public void Marca_alternativa_conta_como_concluida()
    {
        // O Obsidian permite [/], [>], [-]. Tratá-las como pendentes deixaria a contagem sempre errada.
        Assert.True(Assert.Single(Analisar("- [/] em andamento").Tarefas).Concluida);
    }

    [Fact]
    public void Item_de_lista_sem_caixa_nao_e_tarefa()
    {
        Assert.Empty(Analisar("- só um item de lista").Tarefas);
    }

    // —— APELIDOS E RESUMO ——————————————————————————————————————————————————————————————
    [Fact]
    public void Le_apelidos_do_frontmatter()
    {
        var a = Analisar("---\naliases: [Lei 14.133, NLL]\n---\n\ntexto");
        Assert.Equal(["Lei 14.133", "NLL"], a.Apelidos);
    }

    [Fact]
    public void Resumo_pula_o_frontmatter_e_os_cabecalhos()
    {
        var a = Analisar("---\ntitle: X\n---\n# Cabeçalho\nprimeira linha do corpo\nsegunda");
        Assert.StartsWith("primeira linha do corpo", a.Resumo);
        Assert.DoesNotContain("Cabeçalho", a.Resumo);
        Assert.DoesNotContain("title:", a.Resumo);
    }

    [Fact]
    public void Frontmatter_nao_fechado_nao_e_frontmatter()
    {
        // Um "---" solto no topo sem fechamento comeria a nota inteira como metadado.
        var a = Analisar("---\nisto não fecha\n\n# Título de verdade");
        Assert.False(a.Frontmatter.Existe);
        Assert.Equal("Título de verdade", a.Titulo);
    }

    [Fact]
    public void Traco_no_meio_do_texto_e_linha_horizontal_nao_frontmatter()
    {
        var a = Analisar("# Título\n\ntexto\n\n---\n\nmais texto");
        Assert.False(a.Frontmatter.Existe);
    }

    [Fact]
    public void Nota_vazia_nao_quebra()
    {
        var a = Analisar(string.Empty, "Vazia");
        Assert.Equal("Vazia", a.Titulo);
        Assert.Empty(a.Ligacoes);
        Assert.Empty(a.Etiquetas);
        Assert.Equal(0, a.Palavras);
    }
}
