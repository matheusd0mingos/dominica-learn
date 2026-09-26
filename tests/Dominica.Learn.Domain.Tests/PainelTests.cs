using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Painel;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O painel é a base de uma decisão diária. O risco dele não é errar uma conta — é apresentar um número
/// que parece significar uma coisa e significa outra, porque aí a decisão sai errada e ninguém percebe.
/// </summary>
public class PainelTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 7);

    private static Cartao Inedito(string nota) => new(CaminhoNota.De(nota), "p", "r", 1, 1, null);

    private static Cartao Com(string nota, int intervalo, int facilidade, int venceEmDias, bool suspenso = false) =>
        new(CaminhoNota.De(nota), "p", "r", 1, 1,
            new Agendamento(Hoje.AddDays(venceEmDias), intervalo, facilidade), suspenso);

    private static Dictionary<Materia, int> Notas(params (string Materia, int Quantas)[] itens) =>
        itens.ToDictionary(i => Materia.De(i.Materia), i => i.Quantas);

    private static MateriaNoPainel Achar(RetratoDoEstudo r, string materia) =>
        r.Materias.Single(m => m.Materia.Nome == materia);

    // —— CONTAGENS ——————————————————————————————————————————————————————————————————————

    [Fact]
    public void ContaVencidosIneditosEMaduros()
    {
        var r = PainelDeEstudo.Montar(
            [Inedito("Direito/a.md"), Com("Direito/b.md", 6, 250, -1), Com("Direito/c.md", 40, 250, 30)],
            Notas(("Direito", 3)), [], Hoje, teto: 20);

        var d = Achar(r, "Direito");
        Assert.Equal(1, d.Ineditos);
        Assert.Equal(1, d.Maduros);
        // Só o já respondido conta como "a revisar": o inédito tem coluna própria, e somá-los faria a
        // matéria parecer atrasada quando ela é só nova.
        Assert.Equal(1, d.Vencidos);
    }

    [Fact]
    public void OSuspensoFicaForaDeTUDO()
    {
        // Suspenso não é dívida nem conquista: ele está guardado. Contá-lo em qualquer número do painel
        // faria a pessoa ver trabalho que não existe.
        var r = PainelDeEstudo.Montar(
            [Com("Direito/a.md", 1, 250, -1, suspenso: true), Com("Direito/b.md", 1, 250, -1)],
            Notas(("Direito", 2)), [], Hoje, teto: 20);

        Assert.Equal(1, r.RevisoesVencidas);
        Assert.Equal(1, r.TotalDeCartoes);
        Assert.Equal(1, r.Suspensos);
    }

    [Fact]
    public void MateriaSemCartoesAPARECE()
    {
        // O CASO MAIS ACIONÁVEL DO PAINEL. Uma matéria só com notas não tem cartão nenhum para agrupar —
        // se o painel saísse só dos cartões, ela sumiria justamente por ser o problema.
        var r = PainelDeEstudo.Montar([], Notas(("Português", 7)), [], Hoje, teto: 20);

        var p = Achar(r, "Português");
        Assert.Equal(7, p.Notas);
        Assert.Equal(0, p.Cartoes);
    }

    // —— FACILIDADE ————————————————————————————————————————————————————————————————————

    [Fact]
    public void FacilidadeSaiSoDosCARTOESJARESPONDIDOS()
    {
        // O inédito ainda não custou nada. Incluí-lo na média puxaria todo mundo para 2,50× e apagaria o
        // único sinal de dificuldade que existe sem registro de questões.
        var r = PainelDeEstudo.Montar(
            [Inedito("Direito/a.md"), Com("Direito/b.md", 6, 190, -1)],
            Notas(("Direito", 2)), [], Hoje, teto: 20);

        Assert.Equal(190, Achar(r, "Direito").Facilidade);
    }

    [Fact]
    public void SemNenhumCartaoRespondidoAFacilidadeEhZERO()
    {
        // Zero significa "não há sinal", que é diferente de "está fácil". Devolver 250 aqui seria dizer
        // que a matéria vai bem quando ninguém nunca a revisou.
        var r = PainelDeEstudo.Montar([Inedito("Direito/a.md")], Notas(("Direito", 1)), [], Hoje, teto: 20);

        Assert.Equal(0, Achar(r, "Direito").Facilidade);
    }

    [Fact]
    public void MateriaSemSinalNaoEhAcusadaDeCustar()
    {
        // Facilidade 0 é menor que 250, e uma comparação ingênua acusaria de "está custando" justamente a
        // matéria que ninguém revisou ainda.
        var r = PainelDeEstudo.Montar([Inedito("Direito/a.md")], Notas(("Direito", 1)), [], Hoje, teto: 20);

        Assert.DoesNotContain(r.Atencoes, a => a.Tipo == TipoDeAtencao.MateriaCustando);
    }

    // —— O QUE AGORA ————————————————————————————————————————————————————————————————————

    [Fact]
    public void RevisaoVencidaVemANTESDeTudo()
    {
        // Decisão de produto: é conhecimento JÁ conquistado prestes a ser perdido, e recuperá-lo custa
        // minutos contra as horas que custou aprender.
        var r = PainelDeEstudo.Montar(
            [Com("Direito/a.md", 6, 250, -1)],
            Notas(("Direito", 1), ("Português", 9)), [], Hoje, teto: 20);

        Assert.Equal(TipoDeSugestao.Revisar, r.Agora[0].Tipo);
        Assert.Contains("Direito", r.Agora[0].Titulo);
    }

    [Fact]
    public void TodaSugestaoTemMotivoESCRITO()
    {
        // Sem o motivo, a sugestão é uma ordem — e ordem sem explicação deixa de ser obedecida na
        // primeira vez que a pessoa discorda.
        var r = PainelDeEstudo.Montar(
            [Com("Direito/a.md", 6, 190, -1)],
            Notas(("Direito", 1), ("Português", 4)), ["ICMS"], Hoje, teto: 20);

        Assert.NotEmpty(r.Agora);
        Assert.All(r.Agora, s => Assert.False(string.IsNullOrWhiteSpace(s.Motivo)));
    }

    [Fact]
    public void NoMaximoTresSugestoes()
    {
        var r = PainelDeEstudo.Montar(
            [Com("A/a.md", 6, 190, -1), Com("B/b.md", 6, 200, -1), Com("C/c.md", 6, 210, -1)],
            Notas(("A", 1), ("B", 1), ("C", 1), ("D", 8)), ["ICMS", "ISS", "IPTU"], Hoje, teto: 20);

        Assert.True(r.Agora.Count <= PainelDeEstudo.MaximoDeSugestoes);
    }

    [Fact]
    public void MateriaEscritaSemCartaoVIRASUGESTAO()
    {
        var r = PainelDeEstudo.Montar([], Notas(("Português", 7)), [], Hoje, teto: 20);

        var s = Assert.Single(r.Agora);
        Assert.Equal(TipoDeSugestao.FazerCartoes, s.Tipo);
        Assert.Contains("7", s.Motivo);
    }

    [Fact]
    public void LigacaoQuebradaVIRASUGESTAODEESCREVER()
    {
        // É a lista do que a própria pessoa decidiu estudar e ainda não estudou.
        var r = PainelDeEstudo.Montar([], Notas(), ["ICMS"], Hoje, teto: 20);

        var s = Assert.Single(r.Agora);
        Assert.Equal(TipoDeSugestao.Escrever, s.Tipo);
        Assert.Contains("ICMS", s.Titulo);
    }

    // —— NADA A FAZER ——————————————————————————————————————————————————————————————————

    [Fact]
    public void VaultVazioNaoQuebraENaoSugereNada()
    {
        var r = PainelDeEstudo.Montar([], Notas(), [], Hoje, teto: 20);

        Assert.Empty(r.Agora);
        Assert.Empty(r.Atencoes);
        Assert.Empty(r.Materias);
        Assert.Equal(0, r.ParaHoje);
    }

    [Fact]
    public void DiaEmDiaNaoInventaSugestao()
    {
        // Tudo revisado, cartões em dia, nada por escrever: o painel tem de saber ficar calado. Inventar
        // uma tarefa aqui é como se ensina alguém a ignorar o painel.
        var r = PainelDeEstudo.Montar(
            [Com("Direito/a.md", 30, 260, 20)],
            Notas(("Direito", 1)), [], Hoje, teto: 20);

        Assert.Empty(r.Agora);
    }

    // —— O NÚMERO DA TELA BATE COM A FILA —————————————————————————————————————————————

    [Fact]
    public void ParaHojeRespeitaOTETO()
    {
        // O DEFEITO DA PRIMEIRA VERSÃO. Com 36 inéditos e teto 5, o painel dizia 36 e a fila entregava 2
        // (três já tinham entrado no dia). Dois números da mesma tela discordando é a forma mais rápida
        // de a pessoa parar de confiar no painel.
        var cartoes = Enumerable.Range(1, 36).Select(i => Inedito($"Direito/{i}.md")).ToList();

        var r = PainelDeEstudo.Montar(cartoes, Notas(("Direito", 36)), [], Hoje, teto: 5);

        Assert.Equal(5, r.NovosHoje);
        Assert.Equal(31, r.NovosGuardados);
        Assert.Equal(5, r.ParaHoje);
    }

    [Fact]
    public void ParaHojeSomaRevisaoEEstreia()
    {
        var r = PainelDeEstudo.Montar(
            [Com("Direito/a.md", 6, 250, -1), Inedito("Direito/b.md"), Inedito("Direito/c.md")],
            Notas(("Direito", 3)), [], Hoje, teto: 1);

        Assert.Equal(1, r.RevisoesVencidas);
        Assert.Equal(1, r.NovosHoje);
        Assert.Equal(2, r.ParaHoje);
    }

    [Fact]
    public void SemTetoTodosOsIneditosEntram()
    {
        var cartoes = Enumerable.Range(1, 12).Select(i => Inedito($"Direito/{i}.md")).ToList();

        var r = PainelDeEstudo.Montar(cartoes, Notas(("Direito", 12)), [], Hoje, TetoDeCartoesNovos.SemTeto);

        Assert.Equal(12, r.NovosHoje);
        Assert.Equal(0, r.NovosGuardados);
    }

    [Fact]
    public void MateriaSoDeIneditosTAMBEMVIRASUGESTAODEREVISAR()
    {
        // O ERRO DA SEGUNDA VERSÃO. Ao excluir os inéditos da contagem de "vencidos" — que estava certo —
        // a sugestão passou a ignorar a matéria que era só de cartões novos: o painel anunciava "2 para
        // hoje", o botão dizia "começar a revisão", e o "o que agora" mandava fazer outra coisa.
        var r = PainelDeEstudo.Montar(
            [Inedito("Direito/a.md"), Inedito("Direito/b.md")],
            Notas(("Direito", 2), ("Português", 9)), [], Hoje, teto: 20);

        Assert.Equal(TipoDeSugestao.Revisar, r.Agora[0].Tipo);
        Assert.Contains("Direito", r.Agora[0].Titulo);
        Assert.Contains("ainda não viu", r.Agora[0].Motivo);
    }

    [Fact]
    public void OMotivoAVISAQUANDOOTetoSeguraAMaiorParte()
    {
        // Sem esta frase o motivo promete 35 cartões e a sessão entrega 5 — o mesmo defeito do número
        // grande, na letra miúda.
        var cartoes = Enumerable.Range(1, 35).Select(i => Inedito($"Direito/{i}.md")).ToList();

        var r = PainelDeEstudo.Montar(cartoes, Notas(("Direito", 35)), [], Hoje, teto: 5);

        Assert.Contains("teto segura", r.Agora[0].Motivo);
    }

    [Fact]
    public void SemTetoSegurandoNadaOMotivoNaoInventaRessalva()
    {
        var r = PainelDeEstudo.Montar(
            [Inedito("Direito/a.md")], Notas(("Direito", 1)), [], Hoje, teto: 20);

        Assert.DoesNotContain("teto", r.Agora[0].Motivo);
    }

    [Fact]
    public void ComACotaESGOTADAOIneditoNaoViraSugestao()
    {
        // Sem cota, aquela matéria não entrega nada hoje — sugeri-la seria mandar a pessoa para uma fila
        // vazia.
        var jaFeitos = new[] { Com("X/x.md", 1, 250, 1) };
        var r = PainelDeEstudo.Montar(
            [.. jaFeitos, Inedito("Direito/a.md")],
            Notas(("Direito", 1), ("X", 1)), [], Hoje, teto: 1);

        Assert.DoesNotContain(r.Agora, s => s.Tipo == TipoDeSugestao.Revisar);
    }

    // —— MATÉRIA DE REFERÊNCIA ————————————————————————————————————————————————————————
    //
    // O painel cobra cartões de toda matéria escrita, e essa opinião está certa para estudo. Para a
    // pasta de trabalho — anotação de reunião, procedimento, o que se consulta — ela está errada TODO
    // DIA. E como a cobrança é ordenada por quem tem mais notas, a pasta de trabalho ganha a sugestão
    // principal para sempre, empurrando para baixo a revisão que a pessoa abriu o painel para ver.
    //
    // O painel que cobra todo dia uma coisa que a pessoa decidiu não fazer é o painel que ela aprende a
    // ignorar — e aí ele para de funcionar também para o que importa.

    private static HashSet<Materia> Referencias(params string[] nomes) =>
        nomes.Select(Materia.De).ToHashSet();

    [Fact]
    public void Materia_de_referencia_nao_e_cobrada_por_cartoes()
    {
        var r = PainelDeEstudo.Montar([], Notas(("Trabalho", 40)), [], Hoje, teto: 20, Referencias("Trabalho"));

        Assert.DoesNotContain(r.Agora, s => s.Tipo == TipoDeSugestao.FazerCartoes);
        Assert.DoesNotContain(r.Atencoes, a => a.Tipo == TipoDeAtencao.MateriaSemCartoes);
    }

    [Fact]
    public void Sem_a_marca_ela_continua_sendo_cobrada()
    {
        // O contraponto do teste acima: sem ele, a marca poderia estar desligando a cobrança de todo
        // mundo e os dois testes passariam.
        var r = PainelDeEstudo.Montar([], Notas(("Trabalho", 40)), [], Hoje, teto: 20);

        Assert.Contains(r.Agora, s => s.Tipo == TipoDeSugestao.FazerCartoes);
        Assert.Contains(r.Atencoes, a => a.Tipo == TipoDeAtencao.MateriaSemCartoes);
    }

    [Fact]
    public void A_referencia_nao_cala_as_outras_materias()
    {
        // O DEFEITO QUE ESTE TESTE PEGA: marcar Trabalho e, junto, perder a cobrança de Português.
        // Trabalho tem mais notas, então ele é quem ganharia a única vaga da sugestão — se a exclusão
        // fosse feita DEPOIS de escolher, Português nunca apareceria.
        var r = PainelDeEstudo.Montar([], Notas(("Trabalho", 40), ("Português", 3)), [], Hoje,
            teto: 20, Referencias("Trabalho"));

        var sugestao = Assert.Single(r.Agora, s => s.Tipo == TipoDeSugestao.FazerCartoes);
        Assert.Equal("Português", sugestao.Materia.Nome);
        Assert.Equal("Português", Assert.Single(r.Atencoes, a => a.Tipo == TipoDeAtencao.MateriaSemCartoes).Titulo);
    }

    [Fact]
    public void A_materia_de_referencia_continua_existindo_em_tudo_o_mais()
    {
        // A marca silencia UMA cobrança. Ela não esconde a matéria: as notas continuam contadas, a
        // matéria continua na tabela, e some do grafo, da busca e do registro de horas coisa nenhuma.
        // Uma marca que sumisse com a matéria seria usada sem querer e custaria notas de vista.
        var r = PainelDeEstudo.Montar([], Notas(("Trabalho", 40)), [], Hoje, teto: 20, Referencias("Trabalho"));

        var t = Achar(r, "Trabalho");
        Assert.Equal(40, t.Notas);
        Assert.True(t.Referencia);
        Assert.Equal(40, r.TotalDeNotas);
    }

    // —— A PREVISÃO DOS PRÓXIMOS 7 DIAS ———————————————————————————————————————————————
    //
    // O "para hoje" não responde "dá para viajar quinta?". A previsão responde — e só presta se cada
    // cartão aparecer numa contagem SÓ: o vencido já está em RevisoesVencidas, o inédito entra pelo
    // teto. Um cartão contado em dois números da mesma tela é o defeito que já custou confiança duas
    // vezes neste painel.

    [Fact]
    public void A_previsao_tem_sempre_sete_dias_comecando_amanha()
    {
        // Dia sem nada é ZERO na lista, não buraco: "olhei, não tem nada" é informação — uma lista de
        // tamanho variável obrigaria a tela a adivinhar qual dia cada posição é.
        var r = PainelDeEstudo.Montar([], Notas(), [], Hoje, teto: 20);

        Assert.Equal(7, r.ProximosDias.Count);
        Assert.Equal(Hoje.AddDays(1), r.ProximosDias[0].Dia);
        Assert.Equal(Hoje.AddDays(7), r.ProximosDias[^1].Dia);
        Assert.All(r.ProximosDias, d => Assert.Equal(0, d.Cartoes));
    }

    [Fact]
    public void Cada_dia_conta_os_cartoes_que_vencem_NELE()
    {
        var r = PainelDeEstudo.Montar(
            [Com("Direito/a.md", 2, 250, 2), Com("Direito/b.md", 2, 250, 2), Com("Direito/c.md", 5, 250, 5)],
            Notas(("Direito", 3)), [], Hoje, teto: 20);

        Assert.Equal(2, r.ProximosDias[1].Cartoes);   // hoje+2
        Assert.Equal(1, r.ProximosDias[4].Cartoes);   // hoje+5
        Assert.Equal(0, r.ProximosDias[0].Cartoes);
    }

    [Fact]
    public void Vencido_e_o_de_hoje_NAO_entram_na_previsao()
    {
        // Eles já são o "para hoje". Contá-los de novo em "amanhã" somaria o mesmo cartão duas vezes.
        var r = PainelDeEstudo.Montar(
            [Com("Direito/a.md", 1, 250, -3), Com("Direito/b.md", 1, 250, 0)],
            Notas(("Direito", 2)), [], Hoje, teto: 20);

        Assert.Equal(2, r.ParaHoje);
        Assert.All(r.ProximosDias, d => Assert.Equal(0, d.Cartoes));
    }

    [Fact]
    public void Inedito_e_suspenso_ficam_fora_da_previsao()
    {
        // O inédito não tem data — ele estreia conforme o teto do dia, e prever a estreia seria
        // inventar um cronograma que o próprio teto pode mudar. O suspenso está guardado, como em
        // todo o resto do painel.
        var r = PainelDeEstudo.Montar(
            [Inedito("Direito/a.md"), Com("Direito/b.md", 3, 250, 3, suspenso: true)],
            Notas(("Direito", 2)), [], Hoje, teto: 20);

        Assert.All(r.ProximosDias, d => Assert.Equal(0, d.Cartoes));
    }

    [Fact]
    public void O_que_vence_depois_da_semana_fica_fora()
    {
        // A previsão é dos SETE dias — o cartão maduro de trinta dias não pesa em decisão nenhuma
        // desta semana.
        var r = PainelDeEstudo.Montar(
            [Com("Direito/a.md", 30, 260, 8), Com("Direito/b.md", 30, 260, 30)],
            Notas(("Direito", 2)), [], Hoje, teto: 20);

        Assert.All(r.ProximosDias, d => Assert.Equal(0, d.Cartoes));
    }

    [Fact]
    public void Referencia_com_cartoes_continua_entrando_na_revisao()
    {
        // Se a pessoa fez cartões numa matéria de referência, eles são cartões como quaisquer outros:
        // a marca diz "não me cobre", não "ignore o que eu já fiz".
        var r = PainelDeEstudo.Montar(
            [Com("Trabalho/a.md", 6, 250, -1)], Notas(("Trabalho", 5)), [], Hoje,
            teto: 20, Referencias("Trabalho"));

        Assert.Equal(1, r.RevisoesVencidas);
        Assert.Contains(r.Agora, s => s.Tipo == TipoDeSugestao.Revisar && s.Materia.Nome == "Trabalho");
    }

}
