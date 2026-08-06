using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Grafo;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

public class GrafoTests
{
    private static CaminhoNota C(string s) => CaminhoNota.De(s);

    private static LigacaoResolvida Liga(string de, string para) => new()
    {
        Origem = C(de), Alvo = para, Destino = C(para), Forma = FormaDaLigacao.Wikilink,
    };

    [Fact]
    public void Monta_nos_e_arestas()
    {
        var g = GrafoDoVault.Montar([C("A.md"), C("B.md")], [Liga("A.md", "B.md")]);
        Assert.Equal(2, g.Nos.Count);
        var a = Assert.Single(g.Arestas);
        Assert.Equal(1, a.Peso);
    }

    [Fact]
    public void Ligacao_repetida_vira_peso_e_nao_aresta_duplicada()
    {
        // O grafo passa a mostrar INTENSIDADE de relação, não só existência.
        var g = GrafoDoVault.Montar([C("A.md"), C("B.md")], [Liga("A.md", "B.md"), Liga("A.md", "B.md")]);
        Assert.Equal(2, Assert.Single(g.Arestas).Peso);
    }

    [Fact]
    public void Ligacao_quebrada_nao_vira_no()
    {
        // Desenhar um ponto para cada link não escrito encheria o grafo de fantasmas — o oposto do que
        // ele serve para mostrar.
        var quebrada = new LigacaoResolvida { Origem = C("A.md"), Alvo = "NãoExiste", Destino = null };
        var g = GrafoDoVault.Montar([C("A.md")], [quebrada]);
        Assert.Single(g.Nos);
        Assert.Empty(g.Arestas);
    }

    [Fact]
    public void Nota_sem_ligacao_nenhuma_e_marcada_como_orfa()
    {
        // Nota órfã é conhecimento que você produziu e perdeu — por isso é campo, não algo para o olho
        // descobrir no meio da nuvem.
        var g = GrafoDoVault.Montar([C("A.md"), C("B.md"), C("Sozinha.md")], [Liga("A.md", "B.md")]);
        var orfa = Assert.Single(g.Orfas);
        Assert.Equal("Sozinha.md", orfa.Caminho.Valor);
    }

    [Fact]
    public void Autolink_nao_conta()
    {
        var g = GrafoDoVault.Montar([C("A.md")], [Liga("A.md", "A.md")]);
        Assert.Empty(g.Arestas);
        Assert.True(g.Nos[0].Orfa);
    }

    [Fact]
    public void Vizinhanca_recorta_o_que_esta_perto()
    {
        // O grafo INTEIRO de um vault de anos é uma nuvem ilegível. A pergunta real é local.
        var caminhos = new[] { C("A.md"), C("B.md"), C("C.md"), C("Longe.md") };
        var ligacoes = new[] { Liga("A.md", "B.md"), Liga("B.md", "C.md") };
        var g = GrafoDoVault.Montar(caminhos, ligacoes);

        var umSalto = g.Vizinhanca(C("A.md"), saltos: 1);
        Assert.Equal(2, umSalto.Nos.Count);            // A e B

        var doisSaltos = g.Vizinhanca(C("A.md"), saltos: 2);
        Assert.Equal(3, doisSaltos.Nos.Count);         // A, B e C — nunca "Longe"
        Assert.DoesNotContain(doisSaltos.Nos, n => n.Caminho.Valor == "Longe.md");
    }

    [Fact]
    public void Ordem_dos_nos_e_estavel()
    {
        // Grafo que reorganiza a cada carregamento é grafo que ninguém reconhece de um dia para o outro.
        var a = GrafoDoVault.Montar([C("Z.md"), C("A.md")], []);
        var b = GrafoDoVault.Montar([C("A.md"), C("Z.md")], []);
        Assert.Equal(a.Nos.Select(n => n.Caminho.Valor), b.Nos.Select(n => n.Caminho.Valor));
    }

    // —— LAYOUT ————————————————————————————————————————————————————————————————————————
    [Fact]
    public void Layout_e_deterministico_entre_execucoes()
    {
        // A MEMÓRIA ESPACIAL é metade do valor do grafo: "o aglomerado de Direito fica à esquerda". Um
        // layout que sorteia posições joga isso fora a cada abertura.
        var g = GrafoDoVault.Montar([C("A.md"), C("B.md"), C("C.md")], [Liga("A.md", "B.md")]);
        var um = LayoutDeForca.Calcular(g);
        var dois = LayoutDeForca.Calcular(g);
        Assert.Equal(um.Posicoes.Select(p => (p.X, p.Y)), dois.Posicoes.Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void Layout_cabe_dentro_da_area()
    {
        var caminhos = Enumerable.Range(1, 25).Select(i => C($"Nota{i:00}.md")).ToArray();
        var ligacoes = Enumerable.Range(1, 24).Select(i => Liga($"Nota{i:00}.md", $"Nota{i + 1:00}.md")).ToArray();
        var posicionado = LayoutDeForca.Calcular(GrafoDoVault.Montar(caminhos, ligacoes), 800, 600);

        Assert.All(posicionado.Posicoes, p =>
        {
            Assert.InRange(p.X, 0, 800);
            Assert.InRange(p.Y, 0, 600);
        });
    }

    [Fact]
    public void No_mais_ligado_e_maior()
    {
        var caminhos = new[] { C("Hub.md"), C("A.md"), C("B.md"), C("C.md") };
        var ligacoes = new[] { Liga("A.md", "Hub.md"), Liga("B.md", "Hub.md"), Liga("C.md", "Hub.md") };
        var p = LayoutDeForca.Calcular(GrafoDoVault.Montar(caminhos, ligacoes));

        var hub = p.Posicoes[p.Grafo.Nos.Select((n, i) => (n, i)).First(x => x.n.Caminho.Valor == "Hub.md").i];
        var folha = p.Posicoes[p.Grafo.Nos.Select((n, i) => (n, i)).First(x => x.n.Caminho.Valor == "A.md").i];
        Assert.True(hub.Raio > folha.Raio);
    }

    [Fact]
    public void Notas_ligadas_ficam_mais_perto_que_desconexas()
    {
        // É a única coisa que um layout de força PRECISA acertar: aglomerar o que se relaciona.
        var caminhos = new[] { C("A.md"), C("B.md"), C("Solta.md") };
        var p = LayoutDeForca.Calcular(GrafoDoVault.Montar(caminhos, [Liga("A.md", "B.md")]), 800, 600);

        double Dist(string x, string y)
        {
            var i = p.Grafo.Nos.Select((n, k) => (n, k)).First(t => t.n.Caminho.Valor == x).k;
            var j = p.Grafo.Nos.Select((n, k) => (n, k)).First(t => t.n.Caminho.Valor == y).k;
            return Math.Sqrt(Math.Pow(p.Posicoes[i].X - p.Posicoes[j].X, 2) + Math.Pow(p.Posicoes[i].Y - p.Posicoes[j].Y, 2));
        }

        Assert.True(Dist("A.md", "B.md") < Dist("A.md", "Solta.md"));
    }

    [Fact]
    public void Orfas_nao_achatam_o_aglomerado_no_enquadramento()
    {
        // O DEFEITO QUE ESTE TESTE GUARDA, visto na tela antes de existir: um nó sem nenhuma ligação só
        // sente repulsão, e repulsão sem contrapeso o empurra até onde a temperatura deixar. Como o
        // enquadramento final usa a caixa de TODOS os nós, dois órfãos nos cantos encolhem o par ligado a
        // um borrão de poucos pixels — o grafo apaga o que ele serve para mostrar por causa do que ele
        // serve para denunciar.
        var caminhos = new[] { C("A.md"), C("B.md"), C("Orfa1.md"), C("Orfa2.md") };
        var p = LayoutDeForca.Calcular(GrafoDoVault.Montar(caminhos, [Liga("A.md", "B.md")]), 900, 620);

        double Dist(string x, string y)
        {
            var i = p.Grafo.Nos.Select((n, k) => (n, k)).First(t => t.n.Caminho.Valor == x).k;
            var j = p.Grafo.Nos.Select((n, k) => (n, k)).First(t => t.n.Caminho.Valor == y).k;
            return Math.Sqrt(Math.Pow(p.Posicoes[i].X - p.Posicoes[j].X, 2) + Math.Pow(p.Posicoes[i].Y - p.Posicoes[j].Y, 2));
        }

        // Dois nós ligados têm de continuar sendo DOIS nós na tela. 5% da largura é o mínimo para que os
        // círculos não se sobreponham e os rótulos sejam legíveis.
        Assert.True(Dist("A.md", "B.md") > 900 * 0.05,
            $"o par ligado colapsou: {Dist("A.md", "B.md"):0.0} px de distância");
    }

    [Fact]
    public void Nenhum_no_escapa_do_quadro_mesmo_sem_ligacao_nenhuma()
    {
        // Grafo totalmente desconexo é o pior caso da repulsão pura: ninguém puxa ninguém de volta.
        var caminhos = Enumerable.Range(1, 12).Select(i => C($"Solta{i:00}.md")).ToArray();
        var p = LayoutDeForca.Calcular(GrafoDoVault.Montar(caminhos, []), 900, 620);

        Assert.All(p.Posicoes, q =>
        {
            Assert.InRange(q.X, 0, 900);
            Assert.InRange(q.Y, 0, 620);
        });
    }

    [Fact]
    public void Grafo_vazio_e_de_um_no_so_nao_quebram()
    {
        Assert.Empty(LayoutDeForca.Calcular(GrafoDoVault.Vazio).Posicoes);
        var um = LayoutDeForca.Calcular(GrafoDoVault.Montar([C("A.md")], []), 800, 600);
        Assert.Equal((400, 300), (um.Posicoes[0].X, um.Posicoes[0].Y));
    }
}
