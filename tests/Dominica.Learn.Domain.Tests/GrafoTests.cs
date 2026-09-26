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

    // —— MATÉRIA ————————————————————————————————————————————————————————————————————————
    [Fact]
    public void Cada_no_carrega_a_materia_dele()
    {
        var g = GrafoDoVault.Montar([C("Direito/A.md"), C("Português/B.md"), C("Solta.md")], []);
        Assert.Equal("Direito", g.Nos[0].Materia.Nome);
        Assert.Equal("Português", g.Nos[1].Materia.Nome);
        Assert.False(g.Nos[2].Materia.Existe);
    }

    [Fact]
    public void Materias_saem_em_ordem_estavel()
    {
        // A ordem decide a COR de cada matéria na tela. Se ela variasse, "Direito é azul" deixaria de
        // valer entre uma abertura e outra — e é essa associação que faz a legenda ser dispensável
        // depois da primeira semana.
        var g = GrafoDoVault.Montar([C("Português/B.md"), C("Direito/A.md"), C("Direito/C.md")], []);
        Assert.Equal(["Direito", "Português"], g.Materias.Select(m => m.Nome));
    }

    [Fact]
    public void Notas_da_mesma_materia_se_aproximam_mesmo_sem_ligacao()
    {
        // O CASO QUE ISTO RESOLVE: vault de três semanas, muita nota escrita e quase nenhuma ligação
        // feita. Sem coesão por matéria, o grafo é uma nuvem uniforme e a pergunta "como está Direito
        // comparado a Português?" não tem resposta na tela. A pasta existe desde o primeiro arquivo.
        var caminhos = new[]
        {
            C("Direito/A.md"), C("Direito/B.md"), C("Direito/C.md"),
            C("Português/X.md"), C("Português/Y.md"), C("Português/Z.md"),
        };
        var p = LayoutDeForca.Calcular(GrafoDoVault.Montar(caminhos, []), 900, 620);

        double Dist(int i, int j) =>
            Math.Sqrt(Math.Pow(p.Posicoes[i].X - p.Posicoes[j].X, 2) + Math.Pow(p.Posicoes[i].Y - p.Posicoes[j].Y, 2));

        var indices = Enumerable.Range(0, p.Grafo.Nos.Count).ToArray();
        var pares = from i in indices from j in indices where i < j select (i, j);

        var mesma = pares.Where(t => p.Grafo.Nos[t.i].Materia == p.Grafo.Nos[t.j].Materia)
                         .Average(t => Dist(t.i, t.j));
        var outra = pares.Where(t => p.Grafo.Nos[t.i].Materia != p.Grafo.Nos[t.j].Materia)
                         .Average(t => Dist(t.i, t.j));

        Assert.True(mesma < outra, $"mesma matéria {mesma:0.0} px, matérias diferentes {outra:0.0} px");
    }

    [Fact]
    public void Notas_soltas_na_raiz_nao_formam_um_aglomerado()
    {
        // "Sem matéria" NÃO é matéria em comum: duas notas soltas não têm nada a ver uma com a outra só
        // por ainda não terem sido arquivadas. Juntá-las desenharia um agrupamento que não existe.
        var caminhos = new[] { C("A.md"), C("B.md"), C("Direito/X.md"), C("Direito/Y.md") };
        var p = LayoutDeForca.Calcular(GrafoDoVault.Montar(caminhos, []), 900, 620);

        double Dist(int i, int j) =>
            Math.Sqrt(Math.Pow(p.Posicoes[i].X - p.Posicoes[j].X, 2) + Math.Pow(p.Posicoes[i].Y - p.Posicoes[j].Y, 2));

        int Idx(string caminho) => p.Grafo.Nos.Select((n, k) => (n, k)).First(t => t.n.Caminho.Valor == caminho).k;

        Assert.True(Dist(Idx("Direito/X.md"), Idx("Direito/Y.md")) < Dist(Idx("A.md"), Idx("B.md")));
    }

    [Fact]
    public void A_ligacao_pesa_mais_que_a_pasta()
    {
        // A coesão INSINUA o aglomerado; quem o forma é a ligação. Se a pasta mandasse mais, o grafo
        // viraria um explorador de arquivos redondo e pararia de mostrar ideia nenhuma.
        var caminhos = new[] { C("Direito/A.md"), C("Direito/B.md"), C("Português/X.md") };
        var p = LayoutDeForca.Calcular(
            GrafoDoVault.Montar(caminhos, [Liga("Direito/A.md", "Português/X.md")]), 900, 620);

        double Dist(string a, string b)
        {
            int Idx(string c) => p.Grafo.Nos.Select((n, k) => (n, k)).First(t => t.n.Caminho.Valor == c).k;
            var (i, j) = (Idx(a), Idx(b));
            return Math.Sqrt(Math.Pow(p.Posicoes[i].X - p.Posicoes[j].X, 2) + Math.Pow(p.Posicoes[i].Y - p.Posicoes[j].Y, 2));
        }

        // A está LIGADA a X (outra matéria) e apenas na mesma pasta que B.
        Assert.True(Dist("Direito/A.md", "Português/X.md") < Dist("Direito/A.md", "Direito/B.md"));
    }

    // —— RECORTE POR MATÉRIA ——————————————————————————————————————————————————————————
    [Fact]
    public void Recorte_fica_so_com_as_notas_da_materia()
    {
        var g = GrafoDoVault.Montar(
            [C("Direito/A.md"), C("Direito/B.md"), C("Português/X.md")],
            [Liga("Direito/A.md", "Direito/B.md")]);

        var r = g.DaMateria(Materia.De("Direito"));
        Assert.Equal(["Direito/A.md", "Direito/B.md"], r.Grafo.Nos.Select(n => n.Caminho.Valor));
        Assert.Single(r.Grafo.Arestas);
    }

    [Fact]
    public void Ligacao_que_sai_da_materia_e_contada_e_nao_desenhada()
    {
        // Recortar ESCONDE as pontes com outras matérias. Esconder sem avisar faria o recorte parecer o
        // vault inteiro — e a ponte é o achado mais valioso de uma base de estudo: o assunto que a pasta
        // separou e a prova cobra junto.
        var g = GrafoDoVault.Montar(
            [C("Direito/A.md"), C("Direito/B.md"), C("Português/X.md"), C("Português/Y.md")],
            [Liga("Direito/A.md", "Direito/B.md"), Liga("Direito/A.md", "Português/X.md"),
             Liga("Direito/B.md", "Português/Y.md")]);

        var r = g.DaMateria(Materia.De("Direito"));
        Assert.Single(r.Grafo.Arestas);
        Assert.Equal(2, r.LigacoesParaFora);
    }

    [Fact]
    public void Grau_e_recalculado_dentro_do_recorte()
    {
        // A nota que só conversa com OUTRA matéria é órfã dentro desta — e essa é justamente uma das
        // coisas que o recorte serve para mostrar. Manter o grau original a faria parecer conectada.
        var g = GrafoDoVault.Montar(
            [C("Direito/Sozinha.md"), C("Direito/Outra.md"), C("Português/X.md")],
            [Liga("Direito/Sozinha.md", "Português/X.md")]);

        var r = g.DaMateria(Materia.De("Direito"));
        var sozinha = r.Grafo.Nos.First(n => n.Caminho.Valor == "Direito/Sozinha.md");
        Assert.Equal(0, sozinha.Grau);
        Assert.True(sozinha.Orfa);
        Assert.Equal(2, r.Grafo.Orfas.Count());
    }

    [Fact]
    public void Recorte_preserva_titulo_e_materia_dos_nos()
    {
        var titulos = new Dictionary<CaminhoNota, string> { [C("Direito/A.md")] = "Atos Administrativos" };
        var g = GrafoDoVault.Montar([C("Direito/A.md"), C("Português/X.md")], [], titulos);

        var no = Assert.Single(g.DaMateria(Materia.De("Direito")).Grafo.Nos);
        Assert.Equal("Atos Administrativos", no.Titulo);
        Assert.Equal("Direito", no.Materia.Nome);
    }

    [Fact]
    public void Recorte_de_materia_inexistente_e_vazio_e_nao_quebra()
    {
        var g = GrafoDoVault.Montar([C("Direito/A.md")], []);
        var r = g.DaMateria(Materia.De("Astrofísica"));
        Assert.Empty(r.Grafo.Nos);
        Assert.Equal(0, r.LigacoesParaFora);
    }

    [Fact]
    public void Recorte_das_notas_sem_materia_tambem_funciona()
    {
        // "Sem matéria" é a caixa de entrada do vault, e ela precisa ser alcançável para poder encolher.
        var g = GrafoDoVault.Montar([C("Solta.md"), C("Outra.md"), C("Direito/A.md")], []);
        Assert.Equal(2, g.DaMateria(Materia.Nenhuma).Grafo.Nos.Count);
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

    [Fact]
    public void Nenhum_no_cobre_outro_nem_num_aglomerado_denso()
    {
        // O DEFEITO QUE ESTE TESTE GUARDA, visto no estresse com mil notas numa matéria só: a força
        // dirigida equilibra CENTROS e não sabe que os pontos têm raio, e o reenquadramento comprime o
        // resultado para caber. Centros a 6 unidades com raios de 9,5 = um disco sólido da cor da
        // matéria — e zoom nenhum resolve, porque sobreposição sobrevive a zoom. O pior caso é o hub:
        // muitas folhas presas ao mesmo centro, todas puxadas para o mesmo lugar.
        var caminhos = new[] { C("M/Hub.md") }
            .Concat(Enumerable.Range(1, 120).Select(i => C($"M/Folha{i:000}.md")))
            .ToArray();
        var ligacoes = Enumerable.Range(1, 120).Select(i => Liga($"M/Folha{i:000}.md", "M/Hub.md")).ToArray();
        var p = LayoutDeForca.Calcular(GrafoDoVault.Montar(caminhos, ligacoes), 900, 620);

        for (var i = 0; i < p.Posicoes.Count; i++)
        for (var j = i + 1; j < p.Posicoes.Count; j++)
        {
            var a = p.Posicoes[i];
            var b = p.Posicoes[j];
            var dist = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
            // Tolerância de 0,1 só pelo arredondamento a 2 casas — cobertura de verdade não passa nela.
            Assert.True(dist >= a.Raio + b.Raio - 0.1,
                $"nós {i} e {j} se cobrem: {dist:0.0} de distância para raios {a.Raio:0.0}+{b.Raio:0.0}");
        }
    }

    [Fact]
    public void A_moldura_devolvida_contem_o_desenho_inteiro()
    {
        // Separar sobrepostos INCHA o aglomerado, às vezes para além da área pedida. A moldura devolvida
        // tem de acompanhar: é ela que a tela usa como teto da janela (Enquadramento.Ajustar), e uma
        // moldura menor que o desenho corta as bordas — o grafo de carga chegou a aparecer pela metade.
        var caminhos = new[] { C("M/Hub.md") }
            .Concat(Enumerable.Range(1, 120).Select(i => C($"M/Folha{i:000}.md")))
            .ToArray();
        var ligacoes = Enumerable.Range(1, 120).Select(i => Liga($"M/Folha{i:000}.md", "M/Hub.md")).ToArray();
        var p = LayoutDeForca.Calcular(GrafoDoVault.Montar(caminhos, ligacoes), 900, 620);

        var janela = Enquadramento.Ajustar(p.Posicoes, p.Largura, p.Altura);
        Assert.All(p.Posicoes, q =>
        {
            Assert.True(q.X - q.Raio >= janela.X - 0.1 && q.X + q.Raio <= janela.X + janela.Largura + 0.1,
                $"nó {q.Indice} fora da janela no eixo X");
            Assert.True(q.Y - q.Raio >= janela.Y - 0.1 && q.Y + q.Raio <= janela.Y + janela.Altura + 0.1,
                $"nó {q.Indice} fora da janela no eixo Y");
        });
    }
}
