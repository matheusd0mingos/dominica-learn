using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

/// <summary>
/// A REPETIÇÃO ESPAÇADA, do lado da orquestração.
///
/// O agendamento em si (SM-2), a ordem da fila e o teto diário já têm teste no domínio. O que só aqui
/// dá para provar é o que os liga: que responder RELÊ O ARQUIVO antes de agendar, que a marca é gravada
/// NO .md e não numa tabela, que desfazer devolve o cartão ao estado anterior, e que um cartão que saiu
/// do lugar não recebe o agendamento do vizinho.
///
/// E a razão de tudo isso ser gravado no arquivo é a regra que rege o projeto: nada que exista só no
/// índice pode ser conhecimento do usuário. O histórico de revisão de meses é conhecimento.
/// </summary>
public class ServicoDeCartoesTests
{
    private sealed record Cenario(
        ServicoDeCartoes Cartoes, ServicoDeNotas Notas, VaultEmMemoria Vault,
        PreferenciasEmMemoria Preferencias, RegistroEmMemoria Registro, RelogioFixo Relogio);

    private static Cenario Montar()
    {
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero));
        var vault = new VaultEmMemoria(relogio);
        var indice = new IndiceEmMemoria();
        var preferencias = new PreferenciasEmMemoria();
        var registro = new RegistroEmMemoria();
        var rec = new ReconciliarVault(vault, indice, relogio, NullLogger<ReconciliarVault>.Instance);
        var notas = new ServicoDeNotas(vault, indice, new HistoricoEmMemoria(), rec, relogio,
            NullLogger<ServicoDeNotas>.Instance);
        var cartoes = new ServicoDeCartoes(vault, indice, notas, preferencias, registro, relogio,
            NullLogger<ServicoDeCartoes>.Instance);

        return new Cenario(cartoes, notas, vault, preferencias, registro, relogio);
    }

    private static async Task<CaminhoNota> Criar(Cenario c, string caminho, string conteudo)
    {
        var alvo = CaminhoNota.De(caminho);
        Assert.True((await c.Notas.CriarAsync(alvo, conteudo)).Ok);
        return alvo;
    }

    // —— A FILA ————————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Cartao_inedito_entra_na_fila_de_hoje()
    {
        var c = Montar();
        await Criar(c, "Direito/A.md", "# A\n\nO que é prescrição::Perda da pretensão pelo tempo\n");

        var fila = await c.Cartoes.FilaAsync();

        Assert.Single(fila.Cartoes);
    }

    [Fact]
    public async Task Cartao_agendado_para_o_futuro_fica_FORA_da_fila_ate_o_dia()
    {
        var c = Montar();
        await Criar(c, "A.md", "# A\n\nPergunta::Resposta\n");

        var respondida = await c.Cartoes.ResponderAsync(CaminhoNota.De("A.md"), 2, Resposta.Facil);
        Assert.True(respondida.Ok, respondida.Mensagem);
        Assert.Empty((await c.Cartoes.FilaAsync()).Cartoes);

        // e volta quando o dia chega
        c.Relogio.Avancar(TimeSpan.FromDays(respondida.Valor!.IntervaloEmDias + 1));
        Assert.Single((await c.Cartoes.FilaAsync()).Cartoes);
    }

    [Fact]
    public async Task Cartao_suspenso_nao_entra_na_fila_nem_gasta_a_cota_do_dia()
    {
        // Suspenso está GUARDADO, não adiado: não é dívida de revisão nem consome teto.
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n\nSuspensa::Resposta\nNormal::Resposta\n");

        var suspendeu = await c.Cartoes.SuspenderAsync(a, 2, true);
        Assert.True(suspendeu.Ok, suspendeu.Mensagem);

        var fila = await c.Cartoes.FilaAsync();
        Assert.Single(fila.Cartoes);
        Assert.Equal(0, fila.IneditosSegurados);
    }

    [Fact]
    public async Task O_teto_diario_segura_ineditos_E_DIZ_quantos()
    {
        // Um limite que age em silêncio vira defeito aos olhos de quem escreveu 37 cartões e viu 2.
        var c = Montar();
        c.Preferencias.CartoesNovosPorDia = 2;
        await Criar(c, "A.md", "# A\n\nP1::R1\nP2::R2\nP3::R3\nP4::R4\n");

        var fila = await c.Cartoes.FilaAsync();

        Assert.Equal(2, fila.Cartoes.Count);
        Assert.Equal(2, fila.IneditosSegurados);
        Assert.Equal(2, fila.Teto);
    }

    [Fact]
    public async Task Teto_zero_e_sem_teto()
    {
        var c = Montar();
        c.Preferencias.CartoesNovosPorDia = 0;
        await Criar(c, "A.md", "# A\n\nP1::R1\nP2::R2\nP3::R3\n");

        var fila = await c.Cartoes.FilaAsync();

        Assert.Equal(3, fila.Cartoes.Count);
        Assert.Equal(0, fila.IneditosSegurados);
    }

    [Fact]
    public async Task A_fila_de_uma_materia_ignora_as_outras()
    {
        var c = Montar();
        await Criar(c, "Direito/A.md", "# A\n\nP::R\n");
        await Criar(c, "Português/B.md", "# B\n\nP::R\n");

        var fila = await c.Cartoes.FilaAsync(Materia.De("Direito"));

        Assert.Equal("Direito/A.md", Assert.Single(fila.Cartoes).Nota.Valor);
    }

    // —— A FILA POR ETIQUETA ————————————————————————————————————————————————————————

    [Fact]
    public async Task A_fila_de_uma_etiqueta_so_traz_os_cartoes_das_notas_marcadas()
    {
        var c = Montar();
        await Criar(c, "Direito/A.md", "# A\n\n#decorar\n\nP1::R1\n");
        await Criar(c, "Direito/B.md", "# B\n\nP2::R2\n");

        var fila = await c.Cartoes.FilaAsync(etiqueta: Etiqueta.TentarCriar("decorar"));

        Assert.Equal("Direito/A.md", Assert.Single(fila.Cartoes).Nota.Valor);
    }

    [Fact]
    public async Task A_fila_por_etiqueta_entende_a_hierarquia()
    {
        // Revisar #direito TEM de trazer o que está marcado só como #direito/penal — a mesma regra do
        // painel de etiquetas. Duas telas discordando sobre o que "#direito" significa seria pior que
        // nenhuma das duas.
        var c = Montar();
        await Criar(c, "A.md", "# A\n\n#direito/penal\n\nP::R\n");
        await Criar(c, "B.md", "# B\n\n#portugues\n\nP::R\n");

        var fila = await c.Cartoes.FilaAsync(etiqueta: Etiqueta.TentarCriar("direito"));

        Assert.Equal("A.md", Assert.Single(fila.Cartoes).Nota.Valor);
    }

    [Fact]
    public async Task O_ciclo_do_errei_na_prova_fecha()
    {
        // O produto CRIA "#errei-na-prova" sozinho a cada erro registrado. Este teste prova que a ponta
        // solta foi amarrada: registrar o erro e pedir a fila da etiqueta devolve exatamente o cartão
        // que nasceu do erro — "hoje só reviso o que errei" passou a existir.
        var c = Montar();
        await Criar(c, "Direito/Qualquer.md", "# Qualquer\n\nOutro::Cartão\n");
        await c.Cartoes.RegistrarErroAsync(Materia.De("Direito"), "Prescrição x decadência::A primeira extingue a pretensão");

        var fila = await c.Cartoes.FilaAsync(etiqueta: Etiqueta.TentarCriar("errei-na-prova"));

        var cartao = Assert.Single(fila.Cartoes);
        Assert.Contains("Prescrição x decadência", cartao.Frente);
    }

    [Fact]
    public async Task Materia_e_etiqueta_compoem()
    {
        var c = Montar();
        await Criar(c, "Direito/A.md", "# A\n\n#decorar\n\nP1::R1\n");
        await Criar(c, "Português/B.md", "# B\n\n#decorar\n\nP2::R2\n");

        var fila = await c.Cartoes.FilaAsync(Materia.De("Direito"), Etiqueta.TentarCriar("decorar"));

        Assert.Equal("Direito/A.md", Assert.Single(fila.Cartoes).Nota.Valor);
    }

    [Fact]
    public async Task Etiqueta_sem_nota_marcada_da_fila_vazia_e_nao_a_fila_inteira()
    {
        // Degradar para "tudo" aqui seria perigoso: quem pediu "só meus erros" e recebeu o vault
        // inteiro responderia cartões achando que tudo aquilo era erro de prova.
        var c = Montar();
        await Criar(c, "A.md", "# A\n\nP::R\n");

        var fila = await c.Cartoes.FilaAsync(etiqueta: Etiqueta.TentarCriar("nunca-usada"));

        Assert.Empty(fila.Cartoes);
    }

    // —— RESPONDER ————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Responder_grava_a_marca_NO_ARQUIVO()
    {
        // A regra que rege o projeto: histórico de revisão de meses é conhecimento do usuário, e não
        // pode sumir num "reindexar do zero" nem ser invisível no Obsidian Desktop.
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n\nPergunta::Resposta\n");

        var r = await c.Cartoes.ResponderAsync(a, 2, Resposta.Bom);

        Assert.True(r.Ok, r.Mensagem);
        Assert.Contains("<!--", c.Vault.Arquivos["A.md"]);
        Assert.True(r.Valor!.IntervaloEmDias >= 1);
    }

    [Fact]
    public async Task Errar_traz_o_cartao_de_volta_para_hoje()
    {
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n\nPergunta::Resposta\n");

        await c.Cartoes.ResponderAsync(a, 2, Resposta.Errei);

        Assert.Single((await c.Cartoes.FilaAsync()).Cartoes);
    }

    [Fact]
    public async Task Responder_uma_linha_que_nao_tem_cartao_e_RECUSADO()
    {
        // A nota mudou por fora e o cartão não está mais naquela linha. Agendar pela posição levaria o
        // agendamento para o cartão errado, em silêncio — que é o pior resultado possível.
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n\nPergunta::Resposta\n");

        var r = await c.Cartoes.ResponderAsync(a, 99, Resposta.Bom);

        Assert.False(r.Ok);
        Assert.Contains("100", r.Mensagem);   // fala a linha como gente conta, a partir de 1
    }

    [Fact]
    public async Task Responder_numa_nota_que_nao_existe_e_recusado()
    {
        var c = Montar();

        var r = await c.Cartoes.ResponderAsync(CaminhoNota.De("Fantasma.md"), 0, Resposta.Bom);

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Desfazer_devolve_o_cartao_ao_estado_anterior()
    {
        // Responder é a única ação irreversível do ciclo diário, e é feita com o polegar, em sequência.
        // Tocar "Fácil" num cartão que se errou o tira da frente por semanas.
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n\nPergunta::Resposta\n");

        var primeira = await c.Cartoes.ResponderAsync(a, 2, Resposta.Bom);
        var anterior = primeira.Valor;

        await c.Cartoes.ResponderAsync(a, 2, Resposta.Facil);
        var desfez = await c.Cartoes.DesfazerRespostaAsync(a, 2, anterior);

        Assert.True(desfez.Ok, desfez.Mensagem);
        Assert.Contains($"{anterior!.IntervaloEmDias}", c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task Desfazer_um_INEDITO_apaga_a_marca_em_vez_de_inventar_uma()
    {
        // "anterior" nulo não é falta de informação: é a informação de que o cartão era inédito.
        // Reagendar para amanhã criaria um terceiro estado que nunca existiu, e o cartão deixaria de
        // contar como novo para o teto diário, para sempre.
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n\nPergunta::Resposta\n");
        await c.Cartoes.ResponderAsync(a, 2, Resposta.Bom);

        var desfez = await c.Cartoes.DesfazerRespostaAsync(a, 2, anterior: null);

        Assert.True(desfez.Ok, desfez.Mensagem);
        Assert.DoesNotContain("<!--", c.Vault.Arquivos["A.md"]);
        Assert.Single((await c.Cartoes.FilaAsync()).Cartoes);
    }

    // —— CARTÃO NASCIDO DE ERRO ————————————————————————————————————————————————————

    [Fact]
    public async Task Registrar_erro_cria_a_nota_de_erros_da_MATERIA_com_a_etiqueta()
    {
        // Uma nota por matéria, e não uma geral: o erro pertence à matéria, e é lá que precisa aparecer
        // no grafo e na revisão dela. A etiqueta é o que faz "onde eu mais erro?" ter resposta.
        var c = Montar();

        var r = await c.Cartoes.RegistrarErroAsync(Materia.De("Direito"), "Prescrição x decadência::A primeira...");

        Assert.True(r.Ok, r.Mensagem);
        var texto = c.Vault.Arquivos[$"Direito/{ServicoDeCartoes.NotaDeErros}.md"];
        Assert.Contains("#errei-na-prova", texto);
        Assert.Contains("Prescrição x decadência::", texto);
    }

    [Fact]
    public async Task Registrar_erro_de_novo_acrescenta_na_mesma_nota()
    {
        var c = Montar();
        await c.Cartoes.RegistrarErroAsync(Materia.De("Direito"), "Primeiro::erro");

        await c.Cartoes.RegistrarErroAsync(Materia.De("Direito"), "Segundo::erro");

        var texto = c.Vault.Arquivos[$"Direito/{ServicoDeCartoes.NotaDeErros}.md"];
        Assert.Contains("Primeiro::erro", texto);
        Assert.Contains("Segundo::erro", texto);
        Assert.Equal(2, (await c.Cartoes.FilaAsync()).Cartoes.Count);
    }

    [Fact]
    public async Task Registrar_erro_sem_materia_ou_sem_texto_e_recusado()
    {
        var c = Montar();

        Assert.False((await c.Cartoes.RegistrarErroAsync(Materia.Nenhuma, "P::R")).Ok);
        Assert.False((await c.Cartoes.RegistrarErroAsync(Materia.De("Direito"), "  ")).Ok);
        Assert.Empty(c.Vault.Arquivos);
    }

    [Fact]
    public async Task Acrescentar_cartao_NAO_cria_nota()
    {
        // Criar nota é gesto com consequência — ela aparece no grafo, na contagem, na busca — e não pode
        // acontecer como efeito colateral de "criar um cartão".
        var c = Montar();

        var r = await c.Cartoes.AcrescentarCartaoAsync(CaminhoNota.De("Não existe.md"), "P::R");

        Assert.False(r.Ok);
        Assert.Empty(c.Vault.Arquivos);
    }

    [Fact]
    public async Task Acrescentar_cartao_nao_gruda_no_paragrafo_anterior()
    {
        // Colado no fim do parágrafo, o "::" passaria a dividir o texto que já estava lá — e o cartão
        // nasceria com metade da frase de alguém como pergunta.
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n\nUm parágrafo qualquer.");

        await c.Cartoes.AcrescentarCartaoAsync(a, "Pergunta::Resposta");

        Assert.Contains("qualquer.\n\nPergunta::Resposta", c.Vault.Arquivos["A.md"]);
    }

    // —— AS CONTAS DO PAINEL ————————————————————————————————————————————————————————

    [Fact]
    public async Task Por_materia_conta_vencidos_e_total()
    {
        var c = Montar();
        await Criar(c, "Direito/A.md", "# A\n\nP1::R1\nP2::R2\n");
        await Criar(c, "Português/B.md", "# B\n\nP::R\n");
        await c.Cartoes.ResponderAsync(CaminhoNota.De("Direito/A.md"), 2, Resposta.Facil);

        var porMateria = (await c.Cartoes.PorMateriaAsync()).ToDictionary(m => m.Materia.Nome);

        Assert.Equal(2, porMateria["Direito"].Total);
        Assert.Equal(1, porMateria["Direito"].Vencidos);
        Assert.Equal(1, porMateria["Português"].Vencidos);
    }

    [Fact]
    public async Task O_painel_mostra_o_que_a_fila_vai_de_fato_entregar()
    {
        // COM TETO, o total de vencidos e o que a fila entrega DISCORDAM — e dois números da mesma tela
        // discordando é a forma mais rápida de a pessoa parar de confiar no painel. Aconteceu na
        // primeira versão. "ParaHoje" é o número que a tela mostra.
        var c = Montar();
        c.Preferencias.CartoesNovosPorDia = 2;
        await Criar(c, "A.md", "# A\n\nP1::R1\nP2::R2\nP3::R3\nP4::R4\nP5::R5\n");

        var painel = await c.Cartoes.PainelAsync();

        Assert.Equal(5, painel.TotalDeCartoes);
        Assert.Equal(2, painel.NovosHoje);
        Assert.Equal(3, painel.NovosGuardados);
        Assert.Equal(2, painel.ParaHoje);
        Assert.Equal((await c.Cartoes.FilaAsync()).Cartoes.Count, painel.ParaHoje);
    }
}
