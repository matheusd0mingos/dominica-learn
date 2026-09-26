using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

/// <summary>
/// O REGISTRO DE QUESTÕES E HORAS — a metade do produto que sabe ONDE VOCÊ PERDE.
///
/// O cálculo do resumo já tem teste no domínio. O que só aqui se prova é a orquestração: que um
/// lançamento inválido é RECUSADO COM O MOTIVO em vez de entrar torto, que a janela de quatro semanas é
/// aplicada de verdade, e que apagar um lançamento que já sumiu não é tratado como erro.
///
/// A RECUSA COM MOTIVO NÃO É DETALHE. Quem digita "300" no lugar de "30" e recebe "erro" não corrige:
/// para de registrar. E um registro em que ninguém confia deixa de alimentar a fila de revisão, que é o
/// que ele existe para fazer.
/// </summary>
public class ServicoDeDesempenhoTests
{
    private sealed record Cenario(ServicoDeDesempenho Desempenho, RegistroEmMemoria Registro, RelogioFixo Relogio);

    private static Cenario Montar()
    {
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero));
        var registro = new RegistroEmMemoria();
        return new Cenario(
            new ServicoDeDesempenho(registro, relogio, NullLogger<ServicoDeDesempenho>.Instance),
            registro, relogio);
    }

    // —— REGISTRAR ————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Registrar_questoes_guarda_o_lote()
    {
        var c = Montar();

        var r = await c.Desempenho.RegistrarQuestoesAsync(
            Materia.De("Direito"), total: 40, acertos: 26, TimeSpan.FromMinutes(50), "Cebraspe");

        Assert.True(r.Ok, r.Mensagem);
        var lote = Assert.Single(c.Registro.Lotes);
        Assert.Equal(40, lote.Total);
        Assert.Equal(26, lote.Acertos);
        Assert.Equal("Cebraspe", lote.Fonte);
    }

    [Fact]
    public async Task Acertos_maiores_que_o_total_sao_recusados_com_o_motivo()
    {
        var c = Montar();

        var r = await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 10, 11, TimeSpan.Zero, null);

        Assert.False(r.Ok);
        Assert.Contains("acertos", r.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(c.Registro.Lotes);
    }

    [Fact]
    public async Task Lote_sem_materia_e_recusado_dizendo_para_que_ela_serve()
    {
        // A matéria é o que responde "onde eu estou perdendo" — sem ela o lançamento não alimenta nada.
        var c = Montar();

        var r = await c.Desempenho.RegistrarQuestoesAsync(Materia.Nenhuma, 10, 5, TimeSpan.Zero, null);

        Assert.False(r.Ok);
        Assert.Contains("matéria", r.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Total_zero_e_recusado()
    {
        var c = Montar();

        Assert.False((await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 0, 0, TimeSpan.Zero, null)).Ok);
        Assert.Empty(c.Registro.Lotes);
    }

    [Fact]
    public async Task Registrar_sessao_guarda_a_duracao()
    {
        var c = Montar();

        var r = await c.Desempenho.RegistrarSessaoAsync(Materia.De("Direito"), TimeSpan.FromMinutes(50), "revisão");

        Assert.True(r.Ok, r.Mensagem);
        Assert.Equal(TimeSpan.FromMinutes(50), Assert.Single(c.Registro.Sessoes).Duracao);
    }

    [Fact]
    public async Task Sessao_de_duracao_impossivel_e_recusada()
    {
        var c = Montar();

        Assert.False((await c.Desempenho.RegistrarSessaoAsync(Materia.De("Direito"), TimeSpan.Zero, null)).Ok);
        Assert.False((await c.Desempenho.RegistrarSessaoAsync(Materia.De("Direito"), TimeSpan.FromDays(2), null)).Ok);
        Assert.Empty(c.Registro.Sessoes);
    }

    // —— O RESUMO ————————————————————————————————————————————————————————————————

    [Fact]
    public async Task O_resumo_soma_questoes_acertos_e_horas()
    {
        var c = Montar();
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 40, 30, TimeSpan.Zero, null);
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Português"), 20, 10, TimeSpan.Zero, null);
        await c.Desempenho.RegistrarSessaoAsync(Materia.De("Direito"), TimeSpan.FromMinutes(90), null);

        var resumo = await c.Desempenho.ResumoAsync();

        Assert.Equal(60, resumo.Questoes);
        Assert.Equal(40, resumo.Acertos);
        Assert.Equal(TimeSpan.FromMinutes(90), resumo.Horas);
        Assert.Equal(2, resumo.Materias.Count);
    }

    [Fact]
    public async Task A_JANELA_de_quatro_semanas_e_aplicada_de_verdade()
    {
        // A janela não é enfeite: um ano diria que você vai bem em algo que azedou em março, e uma
        // semana oscilaria com um único simulado difícil. Ela precisa CORTAR.
        var c = Montar();
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 100, 90, TimeSpan.Zero, null);

        c.Relogio.Avancar(TimeSpan.FromDays(40));
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 10, 3, TimeSpan.Zero, null);

        var resumo = await c.Desempenho.ResumoAsync();

        // Só o lote recente entra — o de 40 dias atrás ficou para trás da janela padrão (28 dias).
        Assert.Equal(10, resumo.Questoes);
        Assert.Equal(3, resumo.Acertos);
    }

    [Fact]
    public async Task Janela_maior_traz_de_volta_o_que_estava_fora()
    {
        var c = Montar();
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 100, 90, TimeSpan.Zero, null);
        c.Relogio.Avancar(TimeSpan.FromDays(40));

        var resumo = await c.Desempenho.ResumoAsync(TimeSpan.FromDays(365));

        Assert.Equal(100, resumo.Questoes);
    }

    [Fact]
    public async Task Sem_registro_nenhum_o_resumo_vem_vazio_e_nao_da_erro()
    {
        var c = Montar();

        var resumo = await c.Desempenho.ResumoAsync();

        Assert.Equal(0, resumo.Questoes);
        Assert.Equal(0, resumo.Percentual);
        Assert.Empty(resumo.Materias);
    }

    [Fact]
    public async Task A_materia_com_pior_acerto_aparece_quando_ha_amostra()
    {
        var c = Montar();
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 50, 45, TimeSpan.Zero, null);
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Português"), 50, 20, TimeSpan.Zero, null);

        var resumo = await c.Desempenho.ResumoAsync();

        Assert.Equal("Português", resumo.Pior?.Materia.Nome);
    }

    // —— DESFAZER UM LANÇAMENTO ————————————————————————————————————————————————————

    [Fact]
    public async Task Os_ultimos_lancamentos_ficam_disponiveis_para_conferir()
    {
        // É o que faz o registro ser usado: um número que entra e não sai é um número em que ninguém
        // confia. Sem esta lista, quem digitou errado para de registrar em vez de corrigir.
        var c = Montar();
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 40, 30, TimeSpan.Zero, null);

        var ultimos = await c.Desempenho.UltimosAsync();

        Assert.Equal(Materia.De("Direito"), Assert.Single(ultimos).Materia);
    }

    [Fact]
    public async Task Apagar_o_que_ja_sumiu_e_NAO_ENCONTRADO_e_nao_falha()
    {
        // Pode ter sido apagado noutra aba. Dizer que falhou faria a pessoa tentar de novo o que já
        // está feito — e desconfiar de um registro que "não apaga".
        var c = Montar();

        var r = await c.Desempenho.ApagarLancamentoAsync(TipoDeLancamento.Questoes, 999);

        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.NaoEncontrada, r.Motivo);
    }

    // —— A JANELA DO HISTÓRICO ————————————————————————————————————————————————————————
    // O painel oferecia uma janela fixa de 8 semanas. Quem estuda há um ano via os últimos dois meses
    // como se fossem tudo — a régua do próprio esforço parava atrás. "Desde o início" precisa sair do
    // registro mais antigo, e é isso que estes testes prendem: um número chutado grande o bastante
    // (520 semanas, digamos) pareceria funcionar até alguém estudar por mais tempo que o chute.

    [Fact]
    public async Task Desde_o_inicio_cobre_a_sessao_mais_antiga()
    {
        var c = Montar();   // relógio fixo em 08/08/2026
        await c.Registro.RegistrarSessaoAsync(Sessao(c, dias: 200));

        var semanas = await c.Desempenho.SemanasDesdeOInicioAsync();

        // 200 dias ≈ 28,6 semanas; com a folga de uma semana, a grade tem de alcançar o dia 200
        Assert.True(semanas >= 30, $"{semanas} semanas não alcançam uma sessão de 200 dias atrás");
        var historico = await c.Desempenho.HorasAsync(semanas);
        Assert.Equal(TimeSpan.FromMinutes(30), historico.Total);   // a sessão antiga ENTROU na grade
    }

    [Fact]
    public async Task A_janela_padrao_deixa_de_fora_o_que_desde_o_inicio_alcanca()
    {
        // CONTROLE do teste acima: sem ele, "desde o início" poderia estar devolvendo a janela padrão
        // e o primeiro teste passaria por acaso, porque a grade de 8 semanas também tem um total.
        var c = Montar();
        await c.Registro.RegistrarSessaoAsync(Sessao(c, dias: 200));

        var padrao = await c.Desempenho.HorasAsync();
        Assert.True(padrao.Vazio, "a sessão de 200 dias atrás não devia caber na janela padrão");
    }

    [Fact]
    public async Task Sem_registro_nenhum_desde_o_inicio_cai_na_janela_padrao()
    {
        // Vault novo: não há começo de onde contar. Devolver 0 ou 1 encolheria a grade a nada — e o
        // painel mostraria uma tira de dois quadradinhos no primeiro dia de uso.
        var c = Montar();
        Assert.Equal(MapaDeHoras.SemanasPadrao,
            await c.Desempenho.SemanasDesdeOInicioAsync());
    }

    [Fact]
    public async Task Desde_o_inicio_tambem_conta_quem_so_revisa_cartao()
    {
        // Quem revisa sem cronometrar não tem UMA sessão — e olhar só as sessões cortaria o histórico
        // dessa pessoa inteiro, em silêncio.
        var c = Montar();
        await c.Registro.RegistrarRevisaoAsync(new RevisaoDeCartao(
            c.Relogio.Agora.AddDays(-120), Materia.De("Contabilidade"),
            Resposta.Bom));

        var semanas = await c.Desempenho.SemanasDesdeOInicioAsync();

        Assert.True(semanas >= 18, $"{semanas} semanas não alcançam uma revisão de 120 dias atrás");
    }

    private static SessaoDeEstudo Sessao(Cenario c, int dias)
    {
        SessaoDeEstudo.TentarCriar(Materia.De("Contabilidade"), c.Relogio.Agora.AddDays(-dias),
            TimeSpan.FromMinutes(30), null, out var s);
        return s!;
    }

    // —— CORRIGIR UM LANÇAMENTO ————————————————————————————————————————————————————————
    // A resposta antiga era "apague e registre de novo". Ela funciona e tem uma janela: dependendo da
    // ordem, sobra duplicata ou não sobra nada — e quem está corrigindo um "300" que era "30" não quer
    // descobrir nenhum dos dois. Estes testes prendem o UPDATE e, principalmente, prendem que ele NÃO
    // é a porta dos fundos por onde entra o que registrar recusaria.

    [Fact]
    public async Task Editar_corrige_o_lancamento_no_lugar()
    {
        var c = Montar();
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), total: 300, acertos: 22, TimeSpan.FromMinutes(40), "QC");
        var antes = Assert.Single(await c.Desempenho.UltimosAsync());

        var r = await c.Desempenho.EditarLancamentoAsync(
            antes.Tipo, antes.Id, Materia.De("Direito"), total: 30, acertos: 22, TimeSpan.FromMinutes(40), "QC", null);

        Assert.True(r.Ok, r.Mensagem);
        var depois = Assert.Single(await c.Desempenho.UltimosAsync());   // continua UM, não virou dois
        Assert.Equal(antes.Id, depois.Id);                              // e é o MESMO lançamento
        Assert.Equal(30, depois.Total);
    }

    [Fact]
    public async Task Editar_nao_muda_a_data_do_lancamento()
    {
        // Corrigir a contagem de ontem não pode mover o estudo para hoje: isso reescreveria o mapa de
        // calor e a sequência de dias por causa de um erro de digitação.
        var c = Montar();
        await c.Registro.RegistrarSessaoAsync(Sessao(c, dias: 3));
        var antes = Assert.Single(await c.Desempenho.UltimosAsync());

        await c.Desempenho.EditarLancamentoAsync(
            antes.Tipo, antes.Id, Materia.De("Contabilidade"), 0, 0, TimeSpan.FromMinutes(90), null, "corrigido");

        var depois = Assert.Single(await c.Desempenho.UltimosAsync());
        Assert.Equal(antes.Em, depois.Em);
        Assert.Equal(TimeSpan.FromMinutes(90), depois.Tempo);
    }

    [Fact]
    public async Task Editar_recusa_o_que_registrar_recusaria()
    {
        // O CONTROLE que dá sentido aos dois acima. Se a edição pulasse o domínio, este lote passaria
        // com mais acertos que questões — e um percentual acima de 100% entraria no "onde você perde"
        // sem nenhum caminho por onde ter entrado.
        var c = Montar();
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 40, 26, TimeSpan.Zero, null);
        var l = Assert.Single(await c.Desempenho.UltimosAsync());

        var r = await c.Desempenho.EditarLancamentoAsync(
            l.Tipo, l.Id, Materia.De("Direito"), total: 10, acertos: 11, TimeSpan.Zero, null, null);

        Assert.False(r.Ok);
        Assert.Contains("acertos", r.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(40, Assert.Single(await c.Desempenho.UltimosAsync()).Total);   // nada mudou
    }

    [Fact]
    public async Task Editar_lancamento_que_sumiu_nao_e_erro()
    {
        // Mesma leitura do apagar: a outra aba pode ter apagado. Dizer "falhou" faria a pessoa tentar
        // de novo o que já não existe.
        var c = Montar();
        var r = await c.Desempenho.EditarLancamentoAsync(
            TipoDeLancamento.Tempo, 999, Materia.De("Direito"), 0, 0, TimeSpan.FromMinutes(30), null, null);

        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.NaoEncontrada, r.Motivo);
    }

    [Fact]
    public async Task Questoes_anexadas_a_uma_sessao_herdam_a_data_dela()
    {
        // O CASO REAL: cronometrou terça, apertou só "parar", e na quinta lembrou que fez 30 questões
        // naquele bloco. Registrar sem data poria as questões na QUINTA — e o "onde você perde" da
        // semana, o mapa e a média por dia sairiam todos deslocados por causa de uma lembrança tardia.
        var c = Montar();
        await c.Registro.RegistrarSessaoAsync(Sessao(c, dias: 2));
        var sessao = Assert.Single(await c.Desempenho.UltimosAsync());

        await c.Desempenho.RegistrarQuestoesAsync(
            Materia.De("Contabilidade"), 30, 22, TimeSpan.Zero, "QC", em: sessao.Em);

        var lote = Assert.Single(
            await c.Desempenho.UltimosAsync(), x => x.Tipo == TipoDeLancamento.Questoes);
        Assert.Equal(sessao.Em, lote.Em);          // no dia da SESSÃO, não hoje
        Assert.NotEqual(c.Relogio.Agora, lote.Em); // e o relógio de hoje é outro — senão o teste não separa nada
    }

    [Fact]
    public async Task Registrar_sem_data_continua_sendo_agora()
    {
        // CONTROLE do de cima: o parâmetro é opcional e o caminho normal não pode ter mudado.
        var c = Montar();
        await c.Desempenho.RegistrarQuestoesAsync(Materia.De("Direito"), 10, 8, TimeSpan.Zero, null);
        Assert.Equal(c.Relogio.Agora, Assert.Single(await c.Desempenho.UltimosAsync()).Em);
    }
}
