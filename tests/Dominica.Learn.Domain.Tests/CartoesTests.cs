using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

public class CartoesTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 6);
    private static CaminhoNota N => CaminhoNota.De("Direito/Licitações.md");

    // —— ANALISADOR ————————————————————————————————————————————————————————————————————
    [Fact]
    public void Cartao_de_uma_linha()
    {
        var c = Assert.Single(AnalisadorDeCartoes.Analisar(N, "Prazo do pregão::10 dias úteis"));
        Assert.Equal("Prazo do pregão", c.Frente);
        Assert.Equal("10 dias úteis", c.Verso);
        Assert.Null(c.Agendamento);
    }

    [Fact]
    public void Cartao_dentro_de_lista_perde_o_marcador()
    {
        // Quem escreve "- Prazo::10 dias" está escrevendo um cartão dentro de uma lista. O "- " é
        // estrutura do Markdown, não parte da pergunta.
        var c = Assert.Single(AnalisadorDeCartoes.Analisar(N, "- Prazo do pregão::10 dias"));
        Assert.Equal("Prazo do pregão", c.Frente);
    }

    [Fact]
    public void Cartao_de_bloco_com_varias_linhas()
    {
        var texto = "O que é ato administrativo?\n?\nManifestação unilateral\nde vontade da Administração.\n\nOutro parágrafo.";
        var c = Assert.Single(AnalisadorDeCartoes.Analisar(N, texto));
        Assert.Equal("O que é ato administrativo?", c.Frente);
        Assert.Equal("Manifestação unilateral\nde vontade da Administração.", c.Verso);
    }

    [Fact]
    public void Bloco_para_na_linha_em_branco_e_nao_come_a_nota()
    {
        // Sem esse limite, um cartão engoliria tudo o que viesse depois dele no arquivo.
        var texto = "Pergunta\n?\nResposta\n\n# Outro assunto\nQue não é resposta de nada.";
        var c = Assert.Single(AnalisadorDeCartoes.Analisar(N, texto));
        Assert.Equal("Resposta", c.Verso);
    }

    [Theory]
    [InlineData("```cpp\nstd::cout << x;\n```")]
    [InlineData("~~~yaml\nchave::valor\n~~~")]
    public void Bloco_de_codigo_nao_vira_cartao(string texto)
    {
        // "a::b" aparece em C++, YAML e anotação de tipo o tempo todo. Sem esta guarda, a fila de revisão
        // encheria de coisa que ninguém escreveu como cartão.
        Assert.Empty(AnalisadorDeCartoes.Analisar(N, texto));
    }

    [Fact]
    public void Cerca_diferente_nao_fecha_o_bloco()
    {
        // Um ~~~ não pode fechar um bloco aberto com ```: se fechasse, o resto da nota seria lido como
        // se ainda fosse código — ou pior, deixaria de ser.
        var texto = "```\nnao::cartao\n~~~\nainda::codigo\n```\nagora::vale";
        var c = Assert.Single(AnalisadorDeCartoes.Analisar(N, texto));
        Assert.Equal("agora", c.Frente);
    }

    [Fact]
    public void Le_o_agendamento_da_mesma_linha()
    {
        var c = Assert.Single(AnalisadorDeCartoes.Analisar(N, "Prazo::10 dias <!--SR:!2026-08-14,6,250-->"));
        Assert.Equal(new DateOnly(2026, 8, 14), c.Agendamento!.Vence);
        Assert.Equal(6, c.Agendamento.IntervaloEmDias);
        Assert.Equal(250, c.Agendamento.Facilidade);
        Assert.Equal("10 dias", c.Verso);   // a marca não vaza para o verso
    }

    [Fact]
    public void Le_o_agendamento_da_linha_seguinte()
    {
        // O plugin escreve dos dois jeitos dependendo da versão. Ler só um faria um cartão com histórico
        // ser reagendado do zero.
        var c = Assert.Single(AnalisadorDeCartoes.Analisar(N, "Prazo::10 dias\n<!--SR:!2026-08-14,6,250-->"));
        Assert.Equal(new DateOnly(2026, 8, 14), c.Agendamento!.Vence);
    }

    [Fact]
    public void Marca_corrompida_vira_cartao_novo_em_vez_de_data_inventada()
    {
        // Uma sincronização interrompida trunca a marca. Revisar de novo custa um minuto; adivinhar uma
        // data custaria a confiança no agendamento inteiro.
        var c = Assert.Single(AnalisadorDeCartoes.Analisar(N, "Prazo::10 dias <!--SR:!2026-8,abc-->"));
        Assert.Null(c.Agendamento);
    }

    [Fact]
    public void Linha_sem_resposta_nao_e_cartao()
    {
        Assert.Empty(AnalisadorDeCartoes.Analisar(N, "Titulo::"));
        Assert.Empty(AnalisadorDeCartoes.Analisar(N, "::resposta solta"));
    }

    [Fact]
    public void Varios_cartoes_na_mesma_nota()
    {
        var texto = "# Licitações\n\nPrazo::10 dias\nModalidade::Pregão\n\nO que é?\n?\nProcedimento.";
        Assert.Equal(3, AnalisadorDeCartoes.Analisar(N, texto).Count);
    }

    // —— AGENDADOR ————————————————————————————————————————————————————————————————————
    [Fact]
    public void Cartao_novo_vence_hoje()
    {
        var a = Agendamento.Novo(Hoje);
        Assert.True(a.Vencido(Hoje));
        Assert.Equal(Agendamento.FacilidadePadrao, a.Facilidade);
    }

    [Fact]
    public void Errar_traz_o_cartao_de_volta_hoje_e_nao_amanha()
    {
        // Quem errou precisa reencontrar o cartão na MESMA sessão; empurrar para amanhã é deixar o erro
        // descansar até esfriar.
        var depois = AgendadorSM2.Proximo(new Agendamento(Hoje, 30, 250), Resposta.Errei, Hoje);
        Assert.Equal(Hoje, depois.Vence);
        Assert.Equal(0, depois.IntervaloEmDias);
        Assert.True(depois.Facilidade < 250);
    }

    [Fact]
    public void Os_dois_primeiros_passos_sao_fixos()
    {
        // No começo não há histórico que justifique multiplicação nenhuma.
        var primeiro = AgendadorSM2.Proximo(Agendamento.Novo(Hoje), Resposta.Bom, Hoje);
        Assert.Equal(1, primeiro.IntervaloEmDias);

        var segundo = AgendadorSM2.Proximo(primeiro, Resposta.Bom, Hoje);
        Assert.Equal(3, segundo.IntervaloEmDias);
    }

    [Fact]
    public void Acertar_alonga_o_intervalo()
    {
        var depois = AgendadorSM2.Proximo(new Agendamento(Hoje, 10, 250), Resposta.Bom, Hoje);
        Assert.Equal(25, depois.IntervaloEmDias);            // 10 × 2,5
        Assert.Equal(Hoje.AddDays(25), depois.Vence);
    }

    [Fact]
    public void Dificil_nunca_alonga_o_intervalo()
    {
        // Se foi difícil, o cartão tem de voltar ANTES, não depois. Sem este piso a pessoa veria o
        // sistema se afastando justamente do que ela não sabe.
        var depois = AgendadorSM2.Proximo(new Agendamento(Hoje, 30, 400), Resposta.Dificil, Hoje);
        Assert.True(depois.IntervaloEmDias <= 30, $"intervalo cresceu para {depois.IntervaloEmDias}");
    }

    [Fact]
    public void A_facilidade_tem_piso_e_teto()
    {
        // Abaixo do piso o intervalo praticamente não cresce — que é o certo para algo que você não está
        // aprendendo — mas sem virar divisão por quase-zero.
        var ruim = new Agendamento(Hoje, 1, Agendamento.FacilidadeMinima);
        for (var i = 0; i < 10; i++) ruim = AgendadorSM2.Proximo(ruim, Resposta.Errei, Hoje);
        Assert.Equal(Agendamento.FacilidadeMinima, ruim.Facilidade);

        var bom = new Agendamento(Hoje, 1, 250);
        for (var i = 0; i < 20; i++) bom = AgendadorSM2.Proximo(bom, Resposta.Facil, Hoje);
        Assert.True(bom.Facilidade <= 400);
    }

    [Fact]
    public void Dois_anos_de_acertos_nao_estouram_a_data()
    {
        // Sem teto no intervalo, um cartão fácil revisado por muito tempo estoura o DateOnly e o
        // agendamento vira exceção no meio de uma sessão de estudo.
        var a = Agendamento.Novo(Hoje);
        var dia = Hoje;
        for (var i = 0; i < 60; i++)
        {
            a = AgendadorSM2.Proximo(a, Resposta.Facil, dia);
            dia = a.Vence;
        }
        Assert.True(a.IntervaloEmDias <= 3650);
    }

    // —— MARCA ————————————————————————————————————————————————————————————————————————
    [Fact]
    public void A_marca_vai_e_volta_igual()
    {
        var a = new Agendamento(new DateOnly(2026, 12, 31), 42, 265);
        Assert.True(MarcaDeAgendamento.TentarLer(MarcaDeAgendamento.Escrever(a), out var lido));
        Assert.Equal(a, lido);
    }

    [Fact]
    public void A_data_e_sempre_ISO_independente_da_cultura()
    {
        // O vault sincroniza entre máquinas com idiomas diferentes. Uma data escrita em pt-BR e lida em
        // en-US viraria outro dia, ou nenhum.
        var marca = MarcaDeAgendamento.Escrever(new Agendamento(new DateOnly(2026, 3, 4), 1, 250));
        Assert.Contains("2026-03-04", marca, StringComparison.Ordinal);
    }

    // —— ESCRITOR: A GARANTIA CONTRA O LAÇO DO VIGIA ————————————————————————————————
    [Fact]
    public void Gravar_o_mesmo_agendamento_duas_vezes_devolve_texto_identico()
    {
        // ESTE É O TESTE QUE IMPEDE O LAÇO. Gravar muda o arquivo, o vigia percebe, reconcilia e
        // reindexa. O que faz o ciclo TERMINAR é a segunda gravação produzir texto idêntico: a impressão
        // digital não muda, a reconciliação conclui "nada mudou" e para.
        var original = "Prazo::10 dias";
        var a = new Agendamento(new DateOnly(2026, 8, 14), 6, 250);

        var uma = EscritorDeAgendamento.Aplicar(original, 0, a);
        var duas = EscritorDeAgendamento.Aplicar(uma, 0, a);

        Assert.Equal(uma, duas);
        Assert.Same(uma, duas);   // mesma instância: nada foi reescrito
    }

    [Fact]
    public void Gravar_nao_encosta_no_resto_da_nota()
    {
        // Um cartão revisado não pode reformatar o parágrafo ao lado nem mexer no frontmatter. Se
        // mexesse, cada revisão marcaria a nota inteira como alterada.
        var original = "---\ntags: [direito]\n---\n# Licitações\n\nTexto antes.\n\nPrazo::10 dias\n\nTexto depois.";
        var novo = EscritorDeAgendamento.Aplicar(original, 6, new Agendamento(new DateOnly(2026, 8, 14), 6, 250));

        var linhasA = original.Split('\n');
        var linhasB = novo.Split('\n');
        Assert.Equal(linhasA.Length, linhasB.Length);
        for (var i = 0; i < linhasA.Length; i++)
            if (i != 6) Assert.Equal(linhasA[i], linhasB[i]);

        Assert.Contains("<!--SR:!2026-08-14,6,250-->", linhasB[6], StringComparison.Ordinal);
    }

    [Fact]
    public void Regravar_substitui_a_marca_em_vez_de_empilhar()
    {
        // Sem isto, cada revisão acrescentaria um comentário e depois de um mês a linha teria trinta
        // marcas — e o arquivo cresceria para sempre.
        var texto = "Prazo::10 dias <!--SR:!2026-08-14,6,250-->";
        var novo = EscritorDeAgendamento.Aplicar(texto, 0, new Agendamento(new DateOnly(2026, 9, 1), 18, 265));

        Assert.Equal(1, novo.Split(MarcaDeAgendamento.Prefixo).Length - 1);
        Assert.Contains("2026-09-01", novo, StringComparison.Ordinal);
    }

    [Fact]
    public void O_final_de_linha_do_arquivo_e_preservado()
    {
        // Trocar CRLF por LF marcaria TODAS as linhas como alteradas na próxima comparação: uma revisão
        // de um cartão faria a nota inteira parecer reescrita, e a reconciliação nunca assentaria.
        var texto = "Linha um\r\nPrazo::10 dias\r\nLinha três";
        var novo = EscritorDeAgendamento.Aplicar(texto, 1, new Agendamento(new DateOnly(2026, 8, 14), 6, 250));

        Assert.Contains("\r\n", novo, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", novo.Replace("\r\n", "|"), StringComparison.Ordinal);
    }

    [Fact]
    public void O_ciclo_completo_converge()
    {
        // O caminho de verdade: analisa, agenda, grava, reanalisa. O agendamento lido de volta é o mesmo
        // que foi escrito, e gravar de novo não muda mais nada. É a prova de que o vigia acorda uma vez
        // por revisão, e não para sempre.
        var conteudo = "# Licitações\n\nPrazo do pregão::10 dias úteis\n";

        var cartao = Assert.Single(AnalisadorDeCartoes.Analisar(N, conteudo));
        var agendado = AgendadorSM2.Proximo(cartao.AgendamentoOu(Hoje), Resposta.Bom, Hoje);
        var gravado = EscritorDeAgendamento.Aplicar(conteudo, cartao.Linha, agendado);

        var relido = Assert.Single(AnalisadorDeCartoes.Analisar(N, gravado));
        Assert.Equal(agendado, relido.Agendamento);
        Assert.Equal("Prazo do pregão", relido.Frente);
        Assert.Equal("10 dias úteis", relido.Verso);

        Assert.Same(gravado, EscritorDeAgendamento.Aplicar(gravado, relido.Linha, agendado));
    }
}
