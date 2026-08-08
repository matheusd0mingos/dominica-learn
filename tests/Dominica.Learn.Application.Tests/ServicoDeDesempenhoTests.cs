using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
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
}
