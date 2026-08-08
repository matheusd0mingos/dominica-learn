using Dominica.Learn.Domain.Grafo;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// QUEM GANHA A VAGA DE RÓTULO. A regra decide o que a pessoa consegue ler num grafo cheio — e o
/// mecanismo anterior (contar nós) produzia nove pares sobrepostos num vault de 36 notas.
/// </summary>
public class RotulosDoGrafoTests
{
    private static PosicaoDoNo No(int i, double x, double y, double raio = 8) => new(i, x, y, raio);

    private static CandidatoARotulo Quer(int i, string texto, int prioridade = 0) => new(i, texto, prioridade);

    [Fact]
    public void Nos_bem_separados_recebem_todos_os_rotulos()
    {
        var escolhidos = RotulosDoGrafo.Escolher(
            [No(0, 100, 100), No(1, 500, 100), No(2, 100, 400)],
            [Quer(0, "Um"), Quer(1, "Dois"), Quer(2, "Três")]);

        Assert.Equal(3, escolhidos.Count);
    }

    [Fact]
    public void Dois_nomes_que_se_sobreporiam_embaixo_usam_o_de_cima()
    {
        // Os pontos estão separados; os NOMES é que não cabem lado a lado — o caso que o mecanismo
        // antigo não via, porque contava pontos. A saída não é apagar um: é subir o segundo, que
        // continua colado no dono dele.
        var escolhidos = RotulosDoGrafo.Escolher(
            [No(0, 100, 100), No(1, 140, 100)],
            [Quer(0, "CPC 48 — Instrumentos…"), Quer(1, "CPC 36 — Consolidação")]);

        Assert.Equal(2, escolhidos.Count);
        Assert.Equal(LadoDoRotulo.Abaixo, escolhidos[0]);
        Assert.Equal(LadoDoRotulo.Acima, escolhidos[1]);
    }

    [Fact]
    public void Quando_nem_embaixo_nem_em_cima_cabe_o_nome_nao_aparece()
    {
        // Três nomes longos empilhados: o primeiro fica embaixo, o segundo sobe, e o terceiro não tem
        // para onde ir. Ele SOME — porque a alternativa seria deslocá-lo para longe do ponto, e aí
        // ninguém saberia de quem é o nome. Rótulo ambíguo é pior que rótulo ausente.
        var escolhidos = RotulosDoGrafo.Escolher(
            [No(0, 100, 100), No(1, 140, 100), No(2, 180, 100)],
            [Quer(0, "CPC 48 — Instrumentos…", 3), Quer(1, "CPC 36 — Consolidação", 2),
             Quer(2, "CPC 18 — Equivalência…", 1)]);

        Assert.Equal(2, escolhidos.Count);
        Assert.DoesNotContain(2, escolhidos.Keys);
    }

    [Fact]
    public void Quem_tem_PRIORIDADE_maior_escolhe_primeiro()
    {
        // O que fica de fora, ou vai para a segunda opção, é sempre o menos importante — nunca "o que
        // calhou". A nota aberta fica com o lugar preferido (embaixo, onde o olho procura); o vizinho
        // qualquer é que sobe.
        var escolhidos = RotulosDoGrafo.Escolher(
            [No(0, 100, 100), No(1, 130, 100)],
            [Quer(0, "Sem importância", prioridade: 0), Quer(1, "A nota aberta", prioridade: 100)]);

        Assert.Equal(LadoDoRotulo.Abaixo, escolhidos[1]);
        Assert.Equal(LadoDoRotulo.Acima, escolhidos[0]);
    }

    [Fact]
    public void No_empate_ganha_o_de_indice_menor_e_a_escolha_e_ESTAVEL()
    {
        // Sem desempate estável, dois nós de mesma importância trocariam de vaga entre uma abertura e
        // outra — e o grafo passaria a mostrar nomes diferentes a cada visita, destruindo a memória
        // espacial que é a razão de ele existir.
        var posicoes = new[] { No(0, 100, 100), No(1, 130, 100) };
        var candidatos = new[] { Quer(0, "Mesmo peso A"), Quer(1, "Mesmo peso B") };

        var primeira = RotulosDoGrafo.Escolher(posicoes, candidatos);
        var segunda = RotulosDoGrafo.Escolher(posicoes, [.. candidatos.Reverse()]);

        Assert.Equal(LadoDoRotulo.Abaixo, primeira[0]);
        Assert.Equal(primeira, segunda);
    }

    [Fact]
    public void A_largura_sai_do_TEXTO_e_nao_de_um_numero_fixo_por_no()
    {
        // É o comprimento do nome que enche a tela, e é ele que precisa ser medido. Curtos convivem
        // embaixo; longos disputam, e o segundo tem de subir.
        var posicoes = new[] { No(0, 100, 100), No(1, 160, 100) };

        var curtos = RotulosDoGrafo.Escolher(posicoes, [Quer(0, "Ab"), Quer(1, "Cd")]);
        Assert.All(curtos.Values, l => Assert.Equal(LadoDoRotulo.Abaixo, l));

        var longos = RotulosDoGrafo.Escolher(posicoes,
            [Quer(0, "Um nome bem comprido"), Quer(1, "Outro nome comprido")]);
        Assert.Equal(LadoDoRotulo.Acima, longos[1]);
    }

    [Fact]
    public void Nos_na_mesma_coluna_mas_longe_na_vertical_cabem_os_dois()
    {
        // A caixa tem altura de uma linha: dois nomes um sobre o outro só brigam se estiverem perto de
        // verdade. Sem isso, uma coluna de notas perderia todos os rótulos menos um.
        var escolhidos = RotulosDoGrafo.Escolher(
            [No(0, 100, 100), No(1, 100, 200)],
            [Quer(0, "Um nome comprido"), Quer(1, "Outro nome comprido")]);

        Assert.Equal(2, escolhidos.Count);
    }

    [Fact]
    public void O_raio_do_ponto_entra_na_conta()
    {
        // Ponto grande empurra o rótulo dele mais para longe do centro. Ignorar o raio faria dois nós de
        // tamanhos diferentes serem julgados como se fossem do mesmo — e o de raio 30 tem o rótulo
        // caindo bem em cima do vizinho pequeno logo abaixo.
        var escolhidos = RotulosDoGrafo.Escolher(
            [No(0, 100, 100, raio: 30), No(1, 100, 140, raio: 4)],
            [Quer(0, "Nome comprido aqui", 10), Quer(1, "Outro nome comprido", 1)]);

        // O rótulo do ponto GRANDE nasceria a 41 unidades do centro dele — em cima do vizinho pequeno.
        // Ele sobe, e aí o pequeno fica com o lugar de baixo. Ignorar o raio julgaria os dois como se
        // fossem do mesmo tamanho e deixaria o nome atravessando a bolinha.
        Assert.Equal(LadoDoRotulo.Acima, escolhidos[0]);
        Assert.Equal(LadoDoRotulo.Abaixo, escolhidos[1]);
    }

    [Fact]
    public void Fonte_maior_faz_caber_menos_rotulos()
    {
        // É o que o iOS provoca ao inflar o texto sem inflar o desenho. Com a fonte declarada de fato,
        // a decisão acompanha — em vez de rotular como se o texto fosse pequeno e sobrepor tudo.
        var posicoes = new[] { No(0, 100, 100), No(1, 200, 100) };
        var candidatos = new[] { Quer(0, "Nome médio"), Quer(1, "Outro médio") };

        Assert.Equal(2, RotulosDoGrafo.Escolher(posicoes, candidatos, tamanhoDaFonte: 10).Count);
        Assert.Single(RotulosDoGrafo.Escolher(posicoes, candidatos, tamanhoDaFonte: 26));
    }

    [Fact]
    public void Candidato_sem_posicao_ou_sem_texto_e_ignorado_sem_estourar()
    {
        var escolhidos = RotulosDoGrafo.Escolher(
            [No(0, 100, 100)],
            [Quer(0, ""), Quer(99, "Não existe")]);

        Assert.Empty(escolhidos);
    }

    [Fact]
    public void O_rotulo_nao_passa_por_cima_de_OUTRO_ponto()
    {
        // O DEFEITO QUE SÓ APARECEU NA TELA, depois de os rótulos já não baterem entre si: num
        // aglomerado denso os nomes atravessavam as BOLINHAS vizinhas — "CPC 06 — Arrendamentos"
        // cortado ao meio por um ponto laranja. Medir texto contra texto declara vitória cedo demais.
        var escolhidos = RotulosDoGrafo.Escolher(
            [No(0, 100, 100, raio: 6), No(1, 150, 118, raio: 14)],
            [Quer(0, "Nome comprido que passa por baixo", prioridade: 0), Quer(1, "Vizinho", prioridade: 10)]);

        // O nome comprido não passa por baixo (bateria no ponto vizinho) — ele SOBE.
        Assert.Equal(LadoDoRotulo.Acima, escolhidos[0]);
        Assert.Equal(LadoDoRotulo.Abaixo, escolhidos[1]);
    }

    [Fact]
    public void O_proprio_ponto_nao_impede_o_rotulo_dele()
    {
        // O rótulo nasce logo abaixo do dono — encostar nele é o desenho pretendido. Contá-lo como
        // obstáculo faria todo ponto grande perder o nome, que é o oposto do que se quer.
        var escolhidos = RotulosDoGrafo.Escolher(
            [No(0, 100, 100, raio: 40)],
            [Quer(0, "Ponto enorme")]);

        Assert.Single(escolhidos);
    }

    [Fact]
    public void Sem_candidato_nenhum_nao_ha_rotulo()
    {
        Assert.Empty(RotulosDoGrafo.Escolher([No(0, 1, 1)], []));
    }
}
