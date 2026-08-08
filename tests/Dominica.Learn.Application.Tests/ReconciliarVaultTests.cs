using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

/// <summary>
/// A CLASSE QUE MANTÉM ÍNDICE E DISCO DE ACORDO — e o disco é a verdade, sempre: o vault é do usuário,
/// e ele pode mexer nele pelo Obsidian, pelo Finder ou por um git pull enquanto este app dorme.
/// </summary>
public class ReconciliarVaultTests
{
    private sealed record Cenario(
        ReconciliarVault Reconciliacao, ServicoDeNotas Notas, VaultEmMemoria Vault, IndiceEmMemoria Indice);

    private static Cenario Montar()
    {
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero));
        var vault = new VaultEmMemoria(relogio);
        var indice = new IndiceEmMemoria();
        var rec = new ReconciliarVault(vault, indice, relogio, NullLogger<ReconciliarVault>.Instance);
        var notas = new ServicoDeNotas(vault, indice, new HistoricoEmMemoria(), rec, relogio,
            NullLogger<ServicoDeNotas>.Instance);
        return new Cenario(rec, notas, vault, indice);
    }

    private static async Task<bool> Quebrada(Cenario c, string onde) =>
        (await c.Indice.LigacoesDeAsync(CaminhoNota.De(onde))).Single().Quebrada;

    // —— O DEFEITO QUE DURAVA PARA SEMPRE ————————————————————————————————————————————

    [Fact]
    public async Task Criar_a_nota_que_faltava_conserta_a_ligacao_de_quem_ja_a_citava()
    {
        // O FLUXO NORMAL DESTE PRODUTO, e ele estava quebrado de ponta a ponta:
        //   1. escreve-se "ver [[Prescrição]]" antes de a nota existir — o link nasce quebrado, certo,
        //      e vira item da lista "Ainda por escrever";
        //   2. clica-se nesse item, o que cria a nota;
        //   3. e a ligação continuava quebrada NO ÍNDICE. Para sempre.
        //
        // Nem a reconciliação do arranque salvava: disco e índice já concordam, não há o que reconciliar.
        // O grafo nunca desenhava a aresta e o backlink nunca aparecia — até alguém reeditar a nota que
        // citava, à mão, sem saber por quê.
        var c = Montar();
        await c.Notas.CriarAsync(CaminhoNota.De("Tributário.md"), "# Tributário\n\nver [[Prescrição]]\n");
        Assert.True(await Quebrada(c, "Tributário.md"));

        await c.Notas.CriarAsync(CaminhoNota.De("Prescrição.md"), "# Prescrição\n");

        Assert.False(await Quebrada(c, "Tributário.md"));
        var backlinks = await c.Indice.BacklinksAsync(CaminhoNota.De("Prescrição.md"));
        Assert.Equal("Tributário.md", Assert.Single(backlinks).Origem.Valor);
    }

    [Fact]
    public async Task Nota_criada_POR_FORA_tambem_recolhe_as_ligacoes_que_ja_a_esperavam()
    {
        // O mesmo caso pela outra porta: o arquivo apareceu no disco sem passar por este app — arrastado
        // para a pasta, vindo do Obsidian do notebook, ou de um git pull. Quem descobre é a reconciliação.
        var c = Montar();
        await c.Notas.CriarAsync(CaminhoNota.De("Tributário.md"), "# Tributário\n\nver [[Prescrição]]\n");
        c.Vault.Arquivos["Prescrição.md"] = "# Prescrição\n";

        await c.Reconciliacao.ExecutarAsync();

        Assert.False(await Quebrada(c, "Tributário.md"));
    }

    [Fact]
    public async Task Ligacao_que_continua_sem_destino_permanece_quebrada()
    {
        // O contrapeso: consertar não pode virar "resolver na marra". Uma citação do que ainda não
        // existe TEM de continuar quebrada — ela é a lista do que a pessoa decidiu estudar.
        var c = Montar();
        await c.Notas.CriarAsync(CaminhoNota.De("A.md"), "# A\n\nver [[Nunca escrita]]\n");

        await c.Notas.CriarAsync(CaminhoNota.De("Outra.md"), "# Outra\n");
        await c.Reconciliacao.ExecutarAsync();

        Assert.True(await Quebrada(c, "A.md"));
    }

    [Fact]
    public async Task Renomear_uma_nota_PARA_o_nome_citado_tambem_conserta()
    {
        // O outro jeito de um destino passar a existir: a nota já estava lá com outro nome. Renomear
        // reconcilia o vault inteiro, então cai no mesmo conserto.
        //
        // E note qual mudança vale: o que decide é o NOME DO ARQUIVO, não a pasta — o resolvedor cai no
        // nome quando o caminho não casa (ver ResolvedorDeWikilinks, passo 3), então mover uma nota de
        // pasta nunca deixa nem conserta um link quebrado. Só renomear.
        var c = Montar();
        await c.Notas.CriarAsync(CaminhoNota.De("Rascunho.md"), "# Rascunho\n");
        await c.Notas.CriarAsync(CaminhoNota.De("A.md"), "# A\n\nver [[Prescrição]]\n");
        Assert.True(await Quebrada(c, "A.md"));

        await c.Notas.RenomearAsync(CaminhoNota.De("Rascunho.md"), CaminhoNota.De("Direito/Prescrição.md"));

        Assert.False(await Quebrada(c, "A.md"));
    }

    // —— O QUE A RECONCILIAÇÃO JÁ FAZIA ——————————————————————————————————————————————

    [Fact]
    public async Task O_disco_e_a_verdade_criou_alterou_e_removeu()
    {
        var c = Montar();
        await c.Notas.CriarAsync(CaminhoNota.De("Fica.md"), "# Fica\n");
        await c.Notas.CriarAsync(CaminhoNota.De("Some.md"), "# Some\n");

        // três mexidas por fora, como o Obsidian faria
        c.Vault.Arquivos["Nova.md"] = "# Nova\n";
        c.Vault.Arquivos["Fica.md"] = "# Fica\n\nagora com mais texto\n";
        c.Vault.Arquivos.Remove("Some.md");

        var r = await c.Reconciliacao.ExecutarAsync();

        Assert.Equal(1, r.Criadas);
        Assert.Equal(1, r.Alteradas);
        Assert.Equal(1, r.Removidas);
        Assert.DoesNotContain("Some.md", c.Indice.Notas.Keys);
        Assert.Contains("Nova.md", c.Indice.Notas.Keys);
    }

    [Fact]
    public async Task Vault_em_dia_nao_reindexa_nada()
    {
        // Roda a cada acordar do vigia. Reindexar sem motivo torraria CPU num vault de anos — e, pior,
        // faria o vigia se acordar de novo, num laço.
        var c = Montar();
        await c.Notas.CriarAsync(CaminhoNota.De("A.md"), "# A\n");

        var r = await c.Reconciliacao.ExecutarAsync();

        Assert.Equal(0, r.Criadas);
        Assert.Equal(0, r.Alteradas);
        Assert.Equal(0, r.Removidas);
    }

    [Fact]
    public async Task Reindexar_uma_nota_resolve_os_wikilinks_dela()
    {
        var c = Montar();
        await c.Notas.CriarAsync(CaminhoNota.De("Destino.md"), "# Destino\n");
        c.Vault.Arquivos["Origem.md"] = "# Origem\n\nver [[Destino]]\n";

        var resolvedor = new ResolvedorDeWikilinks(await c.Indice.NotasConhecidasAsync());
        await c.Reconciliacao.ReindexarAsync(CaminhoNota.De("Origem.md"), resolvedor);

        var ligacoes = await c.Indice.LigacoesDeAsync(CaminhoNota.De("Origem.md"));
        Assert.Equal("Destino.md", Assert.Single(ligacoes).Destino!.Valor);
    }

    [Fact]
    public async Task Nota_que_sumiu_entre_a_varredura_e_a_leitura_nao_derruba_a_reconciliacao()
    {
        // Acontece: o Obsidian está aberto do outro lado. Não é erro — a próxima passada limpa.
        var c = Montar();
        var resolvedor = new ResolvedorDeWikilinks(Array.Empty<NotaConhecida>());

        await c.Reconciliacao.ReindexarAsync(CaminhoNota.De("Fantasma.md"), resolvedor);

        Assert.Empty(c.Indice.Notas);
    }
}
