using Dominica.Learn.Application;
using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

// Testes da CAMADA DE APLICAÇÃO com dublês em memória. Não tocam disco nem banco: o que se prova aqui é
// a ORQUESTRAÇÃO — que salvar arquiva antes de sobrescrever, que conflito é detectado, que renomear
// reescreve as ligações de entrada. Se isso exigisse Postgres, ninguém rodaria.
public class ServicoDeNotasTests
{
    // OS DUBLÊS MORAM EM DublesEmMemoria.cs. Eles nasceram aqui dentro, privados, e foi por isso que os
    // outros quatro casos de uso ficaram sem teste nenhum: escrever o primeiro exigia recriar disco,
    // índice e histórico do zero. Compartilhados, o custo de entrada de um teste novo caiu a zero.

    // —— montagem ————————————————————————————————————————————————————————————————————
    private sealed record Cenario(ServicoDeNotas Servico, VaultEmMemoria Vault, IndiceEmMemoria Indice, HistoricoEmMemoria Historico);

    private static Cenario Montar()
    {
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero));
        var vault = new VaultEmMemoria(relogio);
        var indice = new IndiceEmMemoria();
        var historico = new HistoricoEmMemoria();
        var reconciliacao = new ReconciliarVault(vault, indice, relogio, NullLogger<ReconciliarVault>.Instance);
        var servico = new ServicoDeNotas(vault, indice, historico, reconciliacao, relogio, NullLogger<ServicoDeNotas>.Instance);
        return new Cenario(servico, vault, indice, historico);
    }

    // —— testes ——————————————————————————————————————————————————————————————————————
    [Fact]
    public async Task Criar_grava_e_indexa()
    {
        var c = Montar();
        var r = await c.Servico.CriarAsync(CaminhoNota.De("Direito/Licitações.md"), "# Licitações\n\n#direito");

        Assert.True(r.Ok);
        Assert.Contains("Direito/Licitações.md", c.Vault.Arquivos.Keys);
        Assert.Contains("Direito/Licitações.md", c.Indice.Notas.Keys);
    }

    [Fact]
    public async Task Criar_recusa_quando_ja_existe()
    {
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        await c.Servico.CriarAsync(caminho, "um");

        var r = await c.Servico.CriarAsync(caminho, "outro");
        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.JaExiste, r.Motivo);
        Assert.Equal("um", c.Vault.Arquivos["A.md"]);   // não sobrescreveu
    }

    [Fact]
    public async Task Salvar_arquiva_a_versao_anterior_antes_de_sobrescrever()
    {
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        var criada = await c.Servico.CriarAsync(caminho, "versão um");

        await c.Servico.SalvarAsync(caminho, "versão dois", criada.Valor!.Impressao);

        var revisao = Assert.Single(c.Historico.Revisoes);
        Assert.Equal("versão um", revisao.Conteudo);
        Assert.Equal("versão dois", c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task Salvar_conteudo_identico_nao_escreve_nem_versiona()
    {
        // O autosave dispara a cada poucos segundos: gravar texto idêntico sujaria o histórico, mexeria
        // na data de modificação e acordaria o vigia de arquivos à toa.
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        var criada = await c.Servico.CriarAsync(caminho, "mesmo texto");

        var r = await c.Servico.SalvarAsync(caminho, "mesmo texto", criada.Valor!.Impressao);

        Assert.True(r.Ok);
        Assert.Empty(c.Historico.Revisoes);
    }

    [Fact]
    public async Task Salvar_detecta_conflito_quando_o_disco_mudou_por_fora()
    {
        // O CENÁRIO CENTRAL DO PRODUTO: o Obsidian do notebook gravou enquanto a aba estava aberta.
        // Sobrescrever em silêncio seria apagar meia hora de trabalho de alguém.
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        var criada = await c.Servico.CriarAsync(caminho, "original");
        var impressaoQueOEditorTinha = criada.Valor!.Impressao;

        c.Vault.Arquivos["A.md"] = "escrito pelo Obsidian";   // mudança externa

        var r = await c.Servico.SalvarAsync(caminho, "escrito pelo editor", impressaoQueOEditorTinha);

        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.Conflito, r.Motivo);
        Assert.Equal("escrito pelo Obsidian", c.Vault.Arquivos["A.md"]);   // preservado
    }

    [Fact]
    public async Task Salvar_sem_impressao_sobrescreve_de_proposito()
    {
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        await c.Servico.CriarAsync(caminho, "original");
        c.Vault.Arquivos["A.md"] = "externo";

        var r = await c.Servico.SalvarAsync(caminho, "eu decido", impressaoEsperada: null);

        Assert.True(r.Ok);
        Assert.Equal("eu decido", c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task Renomear_reescreve_as_ligacoes_que_apontavam_para_a_nota()
    {
        // Sem isto, renomear quebraria todas as ligações de entrada — e num vault de estudo a rede de
        // ligações É o conhecimento.
        var c = Montar();
        var alvo = CaminhoNota.De("Direito/Licitações.md");
        var origem = CaminhoNota.De("Resumo.md");
        await c.Servico.CriarAsync(alvo, "# Licitações");
        await c.Servico.CriarAsync(origem, "ver [[Licitações]] e [[Licitações|as regras]]");

        var r = await c.Servico.RenomearAsync(alvo, CaminhoNota.De("Direito/Contratos.md"));

        Assert.True(r.Ok);
        Assert.Equal("ver [[Contratos]] e [[Contratos|as regras]]", c.Vault.Arquivos["Resumo.md"]);
        Assert.DoesNotContain("Direito/Licitações.md", c.Vault.Arquivos.Keys);
    }

    [Fact]
    public async Task Renomear_leva_o_historico_junto()
    {
        var c = Montar();
        var de = CaminhoNota.De("A.md");
        var criada = await c.Servico.CriarAsync(de, "v1");
        await c.Servico.SalvarAsync(de, "v2", criada.Valor!.Impressao);

        var para = CaminhoNota.De("B.md");
        await c.Servico.RenomearAsync(de, para);

        Assert.NotEmpty(await c.Servico.HistoricoAsync(para));
        Assert.Empty(await c.Servico.HistoricoAsync(de));
    }

    [Fact]
    public async Task Renomear_recusa_destino_ocupado()
    {
        var c = Montar();
        await c.Servico.CriarAsync(CaminhoNota.De("A.md"), "a");
        await c.Servico.CriarAsync(CaminhoNota.De("B.md"), "b");

        var r = await c.Servico.RenomearAsync(CaminhoNota.De("A.md"), CaminhoNota.De("B.md"));

        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.JaExiste, r.Motivo);
        Assert.Equal("b", c.Vault.Arquivos["B.md"]);   // não sobrescreveu
    }

    [Fact]
    public async Task Apagar_guarda_a_ultima_versao_no_historico()
    {
        // Quem apagou por engano às onze da noite tem de conseguir recuperar às nove da manhã.
        var c = Montar();
        var caminho = CaminhoNota.De("A.md");
        await c.Servico.CriarAsync(caminho, "conteúdo que não pode sumir");

        var r = await c.Servico.ApagarAsync(caminho);

        Assert.True(r.Ok);
        Assert.DoesNotContain("A.md", c.Vault.Arquivos.Keys);
        Assert.Contains(c.Historico.Revisoes, x => x.Conteudo == "conteúdo que não pode sumir");
    }

    [Fact]
    public async Task Abrir_traz_backlinks_e_ligacoes_quebradas()
    {
        var c = Montar();
        await c.Servico.CriarAsync(CaminhoNota.De("Alvo.md"), "# Alvo");
        await c.Servico.CriarAsync(CaminhoNota.De("Origem.md"), "aponta para [[Alvo]] e para [[NãoExiste]]");

        var alvo = await c.Servico.AbrirAsync(CaminhoNota.De("Alvo.md"));
        Assert.True(alvo.Ok);
        Assert.Single(alvo.Valor!.Backlinks);

        var origem = await c.Servico.AbrirAsync(CaminhoNota.De("Origem.md"));
        Assert.Contains(origem.Valor!.Saidas, l => l.Quebrada && l.Alvo == "NãoExiste");
    }

    [Fact]
    public async Task Abrir_nota_inexistente_devolve_falha_e_nao_excecao()
    {
        var r = await Montar().Servico.AbrirAsync(CaminhoNota.De("Nada.md"));
        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.NaoEncontrada, r.Motivo);
    }

    // —— LIGAR DUAS NOTAS PELO BOTÃO ——————————————————————————————————————————————————
    //
    // A costura: resolver o texto mais curto, escrever na seção, e passar pelo caminho que reindexa.
    // A regra de ONDE escrever é do domínio e tem teste lá (SecaoDeRelacionadasTests); aqui se prova
    // que o caso de uso liga as peças certas — e, principalmente, que a ligação chega ao ÍNDICE, que
    // é de onde o grafo sai. Uma ligação que existe no arquivo e não no índice é uma linha que não
    // aparece no grafo, e é o defeito que este recurso não pode ter.

    private static ServicoDeConhecimento Conhecimento(Cenario c) =>
        new(c.Vault, c.Indice, null!, null!, c.Servico, null!,
            NullLogger<ServicoDeConhecimento>.Instance);

    [Fact]
    public async Task Ligar_escreve_na_nota_e_a_ligacao_chega_ao_indice()
    {
        var c = Montar();
        await c.Servico.CriarAsync(CaminhoNota.De("Direito/Mapa.md"), "# Mapa\n");
        await c.Servico.CriarAsync(CaminhoNota.De("Direito/Crase.md"), "# Crase\n");

        var r = await Conhecimento(c).LigarAsync(CaminhoNota.De("Direito/Mapa.md"), CaminhoNota.De("Direito/Crase.md"));

        Assert.True(r.Ok);
        Assert.False(r.Valor!.JaHavia);
        Assert.Equal("- [[Crase]]", r.Valor.Linha);
        Assert.Contains("## Relacionadas\n- [[Crase]]", c.Vault.Arquivos["Direito/Mapa.md"]);

        // O grafo sai do índice, não do arquivo.
        var ligacoes = await c.Indice.LigacoesDeAsync(CaminhoNota.De("Direito/Mapa.md"));
        Assert.Equal("Direito/Crase.md", Assert.Single(ligacoes).Destino?.Valor);
    }

    [Fact]
    public async Task Com_homonimas_a_ligacao_leva_o_caminho()
    {
        // "[[Crase]]" com duas notas de mesmo nome resolveria para qualquer uma das duas — e o
        // resolvedor desempata por proximidade, o que faria o botão ligar para a nota errada em
        // silêncio. O texto tem de ser o caminho.
        var c = Montar();
        await c.Servico.CriarAsync(CaminhoNota.De("Direito/Mapa.md"), "# Mapa\n");
        await c.Servico.CriarAsync(CaminhoNota.De("Direito/Crase.md"), "# Crase\n");
        await c.Servico.CriarAsync(CaminhoNota.De("Português/Crase.md"), "# Crase\n");

        var r = await Conhecimento(c).LigarAsync(
            CaminhoNota.De("Direito/Mapa.md"), CaminhoNota.De("Português/Crase.md"));

        Assert.Equal("- [[Português/Crase]]", r.Valor!.Linha);
        var ligacoes = await c.Indice.LigacoesDeAsync(CaminhoNota.De("Direito/Mapa.md"));
        Assert.Equal("Português/Crase.md", Assert.Single(ligacoes).Destino?.Valor);
    }

    [Fact]
    public async Task Ligar_de_novo_nao_escreve_e_avisa()
    {
        var c = Montar();
        await c.Servico.CriarAsync(CaminhoNota.De("A.md"), "# A\n");
        await c.Servico.CriarAsync(CaminhoNota.De("B.md"), "# B\n");
        var conhecimento = Conhecimento(c);

        await conhecimento.LigarAsync(CaminhoNota.De("A.md"), CaminhoNota.De("B.md"));
        var antes = c.Vault.Arquivos["A.md"];
        var r = await conhecimento.LigarAsync(CaminhoNota.De("A.md"), CaminhoNota.De("B.md"));

        Assert.True(r.Ok);
        Assert.True(r.Valor!.JaHavia);
        Assert.Equal(antes, c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task A_OUTRA_direcao_e_o_mesmo_metodo()
    {
        // "faça B apontar para cá" é escrever em B. Um método só para as duas direções — dois seriam
        // duas cópias das mesmas regras para manter iguais.
        var c = Montar();
        await c.Servico.CriarAsync(CaminhoNota.De("Aberta.md"), "# Aberta\n");
        await c.Servico.CriarAsync(CaminhoNota.De("Outra.md"), "# Outra\n");

        var r = await Conhecimento(c).LigarAsync(CaminhoNota.De("Outra.md"), CaminhoNota.De("Aberta.md"));

        Assert.Equal(CaminhoNota.De("Outra.md"), r.Valor!.Nota);   // a tela precisa dizer ONDE escreveu
        Assert.Contains("- [[Aberta]]", c.Vault.Arquivos["Outra.md"]);
        Assert.DoesNotContain("Relacionadas", c.Vault.Arquivos["Aberta.md"]);
    }

    [Fact]
    public async Task Nota_nao_aponta_para_ela_mesma()
    {
        var c = Montar();
        await c.Servico.CriarAsync(CaminhoNota.De("A.md"), "# A\n");

        var r = await Conhecimento(c).LigarAsync(CaminhoNota.De("A.md"), CaminhoNota.De("A.md"));

        Assert.False(r.Ok);
        Assert.DoesNotContain("Relacionadas", c.Vault.Arquivos["A.md"]);
    }

}
