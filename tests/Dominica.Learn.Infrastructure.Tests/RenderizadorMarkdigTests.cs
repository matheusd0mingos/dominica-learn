using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Infrastructure.Renderizacao;

namespace Dominica.Learn.Infrastructure.Tests;

// A renderização é o ponto de entrada de XSS do produto: o conteúdo é Markdown colado de qualquer canto
// da internet. Os testes de segurança aqui não são "casos de borda" — são o requisito.
public class RenderizadorMarkdigTests
{
    private readonly RenderizadorMarkdig _r = new();

    private string Render(string md, params string[] existentes)
    {
        var mapa = existentes.Select(CaminhoNota.De).ToList();
        return _r.Renderizar(md, alvo =>
            mapa.FirstOrDefault(c => string.Equals(c.Nome, alvo, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(c.Valor, alvo, StringComparison.OrdinalIgnoreCase))).Html;
    }

    // —— SEGURANÇA ——————————————————————————————————————————————————————————————————————
    [Fact]
    public void Script_colado_na_nota_e_escapado_nao_executado()
    {
        var html = Render("antes <script>alert('xss')</script> depois");
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Atributo_de_evento_em_tag_colada_nao_sobrevive()
    {
        // O vetor clássico de conteúdo colado de um site: <img onerror>.
        var html = Render("<img src=x onerror=\"alert(1)\">");
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror=\"alert", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Iframe_vira_texto_e_isso_e_o_preco_declarado()
    {
        // Quem cola um iframe de vídeo vê o texto do iframe, não o vídeo. É o preço de recusar HTML na
        // origem em vez de sanitizar por lista de bloqueio — e é melhor que o inverso.
        var html = Render("<iframe src=\"https://exemplo\"></iframe>");
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Svg_com_onload_nao_passa()
    {
        var html = Render("<svg onload=alert(1)>");
        Assert.DoesNotContain("<svg", html, StringComparison.OrdinalIgnoreCase);
    }

    // —— WIKILINKS ——————————————————————————————————————————————————————————————————————
    [Fact]
    public void Wikilink_para_nota_existente_vira_link_navegavel()
    {
        var html = Render("ver [[Licitações]]", "Direito/Licitações.md");
        Assert.Contains("href=\"/notas/Direito/Licita", html);
        Assert.DoesNotContain("link-quebrado", html);
    }

    [Fact]
    public void Wikilink_quebrado_vira_link_de_criar_e_e_marcado()
    {
        // Link quebrado continua sendo LINK: clicar é como se cria a nota que falta. Virar texto morto
        // tiraria do produto o fluxo "escrevo o link, depois escrevo a nota".
        var html = Render("ver [[Ainda Não Existe]]");
        Assert.Contains("/notas/novo?nome=", html);
        Assert.Contains("link-quebrado", html);
    }

    [Fact]
    public void Rotulo_do_wikilink_e_preservado()
    {
        var html = Render("[[Licitações|as regras]]", "Licitações.md");
        Assert.Contains(">as regras<", html);
    }

    [Fact]
    public void Wikilink_dentro_de_bloco_de_codigo_nao_vira_link()
    {
        var html = Render("```\n[[NãoÉLink]]\n```", "NãoÉLink.md");
        Assert.DoesNotContain("href=\"/notas/", html);
    }

    // —— ANEXOS ————————————————————————————————————————————————————————————————————————
    // O anexo é referenciado no .md por embed relativo ao vault ("![[foto.png]]"), porque o arquivo tem
    // de continuar valendo aberto no Obsidian. Traduzir isso para uma URL é trabalho DAQUI.
    private string RenderComAnexos(string md, params string[] existentes)
    {
        var mapa = existentes.ToHashSet(StringComparer.Ordinal);
        return _r.Renderizar(md, _ => null,
            alvo => mapa.Contains(alvo) ? "/anexos/Anexos/" + Uri.EscapeDataString(alvo) : null).Html;
    }

    [Fact]
    public void Embed_de_imagem_vira_img_apontando_para_a_rota_de_anexos()
    {
        var html = RenderComAnexos("veja ![[foto.png]] aqui", "foto.png");
        Assert.Contains("<img src=\"/anexos/Anexos/foto.png\"", html);
    }

    [Fact]
    public void Embed_com_rotulo_usa_o_rotulo_como_texto_alternativo()
    {
        var html = RenderComAnexos("![[foto.png|planta do pavimento]]", "foto.png");
        Assert.Contains("alt=\"planta do pavimento\"", html);
    }

    [Fact]
    public void Link_para_anexo_sem_exclamacao_e_link_e_nao_imagem()
    {
        // Um PDF embutido no meio de um resumo é uma parede que empurra o texto para fora da tela.
        var html = RenderComAnexos("[[edital.pdf]]", "edital.pdf");
        Assert.Contains("<a href=\"/anexos/Anexos/edital.pdf\"", html);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Anexo_que_nao_existe_cai_no_fluxo_de_link_quebrado()
    {
        // Referência a arquivo ausente é exatamente isso: uma referência quebrada. Virar <img> com src
        // morto daria um ícone de imagem partida sem dizer o que faltou.
        var html = RenderComAnexos("![[sumiu.png]]");
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("link-quebrado", html);
    }

    [Fact]
    public void Wikilink_de_nota_nao_consulta_o_armazem_de_anexos()
    {
        // A consulta ao armazém toca o disco. Fazê-la a cada "[[Licitações]]" seria uma ida ao disco por
        // wikilink de nota — dezenas por abertura, todas para não achar nada.
        var consultas = new List<string>();
        _r.Renderizar("[[Licitações]] e [[Direito/Contratos]]", _ => CaminhoNota.De("Licitações.md"),
            alvo => { consultas.Add(alvo); return null; });
        Assert.Empty(consultas);
    }

    [Fact]
    public void Sem_resolvedor_de_anexos_o_embed_continua_sendo_tratado_como_nota()
    {
        // Retrocompatibilidade: quem chama a porta sem o terceiro argumento tem o comportamento antigo.
        var html = _r.Renderizar("![[foto.png]]", _ => null).Html;
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Embed_dentro_de_bloco_de_codigo_nao_vira_imagem()
    {
        var html = RenderComAnexos("```\n![[foto.png]]\n```", "foto.png");
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    // —— BLOCOS PARA O CLIENTE ————————————————————————————————————————————————————————
    [Fact]
    public void Bloco_mermaid_e_marcado_para_o_cliente_desenhar()
    {
        var r = _r.Renderizar("```mermaid\ngraph TD; A-->B;\n```", _ => null);
        Assert.True(r.TemDiagramas);
        Assert.Contains("class=\"mermaid\"", r.Html);
    }

    [Fact]
    public void Nota_sem_diagrama_nao_pede_o_mermaid()
    {
        // Não carregar a biblioteca em quem não usa é metade da promessa de "carregamento instantâneo".
        Assert.False(_r.Renderizar("# Só texto", _ => null).TemDiagramas);
    }

    [Fact]
    public void Formula_matematica_e_detectada()
    {
        Assert.True(_r.Renderizar("A energia é $E = mc^2$ mesmo.", _ => null).TemFormulas);
        Assert.True(_r.Renderizar("$$\\int_0^1 x\\,dx$$", _ => null).TemFormulas);
        Assert.False(_r.Renderizar("custou R$ 10 e R$ 20", _ => null).TemFormulas);
    }

    // —— MARKDOWN NORMAL ————————————————————————————————————————————————————————————————
    [Fact]
    public void Tabela_lista_de_tarefas_e_codigo_funcionam()
    {
        Assert.Contains("<table", Render("| a | b |\n|---|---|\n| 1 | 2 |"));
        Assert.Contains("type=\"checkbox\"", Render("- [ ] fazer"));
        Assert.Contains("language-csharp", Render("```csharp\nvar x = 1;\n```"));
    }

    [Fact]
    public void Frontmatter_nao_aparece_no_corpo_renderizado()
    {
        var html = Render("---\ntitle: X\ntags: [a]\n---\n\n# Corpo");
        Assert.DoesNotContain("title:", html);
        Assert.Contains("Corpo", html);
    }

    [Fact]
    public void Nota_vazia_devolve_vazio_sem_quebrar()
    {
        Assert.Equal(string.Empty, _r.Renderizar(string.Empty, _ => null).Html);
        Assert.Equal(string.Empty, _r.Renderizar("   ", _ => null).Html);
    }

    // —— TRANSCLUSÃO ————————————————————————————————————————————————————————————————————
    //
    // ![[Nota]] com provedor de conteúdo embute a nota; sem provedor, ou com a nota inexistente, degrada
    // para link — o comportamento que sempre houve. A profundidade é UM: o embed de dentro do embutido
    // vira link, e é essa a guarda de ciclo.

    private Dominica.Learn.Application.Portas.NotaRenderizada RenderComNotas(
        string md, Dictionary<string, string> vault)
    {
        return _r.Renderizar(md,
            alvo => vault.Keys.FirstOrDefault(c =>
                string.Equals(CaminhoNota.De(c).Nome, alvo, StringComparison.OrdinalIgnoreCase)) is { } achado
                    ? CaminhoNota.De(achado) : null,
            anexos: null,
            notas: alvo => vault.FirstOrDefault(par =>
                string.Equals(CaminhoNota.De(par.Key).Nome, alvo, StringComparison.OrdinalIgnoreCase)).Value);
    }

    [Fact]
    public void Embed_de_nota_embute_o_conteudo_renderizado()
    {
        var r = RenderComNotas("antes\n\n![[Outra]]\n\ndepois",
            new() { ["Direito/Outra.md"] = "# Título da outra\n\ncorpo da outra" });

        Assert.Contains("class=\"transclusao\"", r.Html);
        Assert.Contains("corpo da outra", r.Html);
        // A moldura diz de onde veio, e o link leva lá.
        Assert.Contains("href=\"/notas/Direito/Outra.md\"", r.Html);
    }

    [Fact]
    public void O_embutido_passa_pela_mesma_regra_de_seguranca()
    {
        // O conteúdo embutido é nota como qualquer outra — colada de qualquer canto da internet. Se ele
        // entrasse sem passar pelo DisableHtml, a transclusão seria a porta dos fundos do XSS.
        var r = RenderComNotas("![[Outra]]",
            new() { ["Outra.md"] = "<script>alert('xss')</script>" });

        Assert.DoesNotContain("<script>", r.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", r.Html);
    }

    [Fact]
    public void A_profundidade_e_um_e_o_ciclo_nao_trava()
    {
        // A resposta embute B; B embute A de volta. Sem a guarda, isto é recursão infinita. Com ela, o
        // ![[A]] dentro de B vira LINK — visível, clicável, e o desenho para de descer.
        var r = RenderComNotas("![[B]]", new()
        {
            ["A.md"] = "![[B]]",
            ["B.md"] = "conteúdo de B e ![[A]]",
        });

        Assert.Contains("conteúdo de B", r.Html);
        var aparicoes = r.Html.Split("class=\"transclusao\"").Length - 1;
        Assert.Equal(1, aparicoes);                          // só o embed de fora embutiu
        Assert.Contains("href=\"/notas/A.md\"", r.Html);     // o de dentro virou link
    }

    [Fact]
    public void Embed_de_nota_inexistente_continua_virando_link_quebrado()
    {
        var r = RenderComNotas("![[Não Escrita]]", []);
        Assert.Contains("link-quebrado", r.Html);
        Assert.DoesNotContain("transclusao", r.Html);
    }

    [Fact]
    public void Sem_provedor_de_notas_o_embed_vira_link_como_sempre()
    {
        var caminho = CaminhoNota.De("Outra.md");
        var html = _r.Renderizar("![[Outra]]", _ => caminho).Html;
        Assert.Contains("href=\"/notas/Outra.md\"", html);
        Assert.DoesNotContain("transclusao", html);
    }

    [Fact]
    public void Embed_dentro_de_bloco_de_codigo_nao_e_transcluido()
    {
        var r = RenderComNotas("```\n![[Outra]]\n```", new() { ["Outra.md"] = "conteúdo" });
        Assert.DoesNotContain("transclusao", r.Html);
        Assert.DoesNotContain("conteúdo", r.Html);
    }

    [Fact]
    public void Formula_e_diagrama_do_embutido_contam_para_a_nota_que_embute()
    {
        // A tela decide carregar KaTeX/Mermaid pelo aviso do C#. Se o embutido tem fórmula e o aviso não
        // sobe, a fórmula aparece crua — só dentro da transclusão, o tipo de defeito difícil de notar.
        var r = RenderComNotas("sem fórmula própria\n\n![[Outra]]",
            new() { ["Outra.md"] = "tem $E=mc^2$ e\n\n```mermaid\ngraph TD; A-->B\n```" });

        Assert.True(r.TemFormulas);
        Assert.True(r.TemDiagramas);
    }

    [Fact]
    public void Frontmatter_do_embutido_nao_aparece()
    {
        var r = RenderComNotas("![[Outra]]",
            new() { ["Outra.md"] = "---\ntags: [x]\n---\n\ncorpo" });

        Assert.Contains("corpo", r.Html);
        Assert.DoesNotContain("tags:", r.Html);
    }

    [Fact]
    public void Embed_com_secao_embute_a_nota_inteira_com_a_ancora_no_link()
    {
        // Preço declarado: fatiar a seção exigiria reconhecer onde ela termina, e errar isso corta
        // conteúdo em silêncio. Embute-se a mais, nunca a menos — e o link da moldura leva à seção.
        var r = RenderComNotas("![[Outra#Detalhe]]", new() { ["Outra.md"] = "# Detalhe\n\ncorpo" });

        Assert.Contains("corpo", r.Html);
        Assert.Contains("href=\"/notas/Outra.md#detalhe\"", r.Html);
    }
}
