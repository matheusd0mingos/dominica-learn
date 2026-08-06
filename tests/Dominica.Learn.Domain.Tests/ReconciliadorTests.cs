using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

// A reconciliação é o que sustenta a promessa "o vault abre no Obsidian". Se ela errar, o índice mente
// sobre o disco — e um índice que mente é pior que índice nenhum, porque ninguém desconfia dele.
public class ReconciliadorTests
{
    private static readonly DateTimeOffset Ontem = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Hoje = new(2026, 8, 6, 10, 0, 0, TimeSpan.Zero);

    private static EstadoDaNota Estado(string caminho, string conteudo, DateTimeOffset? quando = null) =>
        new(CaminhoNota.De(caminho), quando ?? Hoje, ImpressaoDigital.De(conteudo));

    [Fact]
    public void Vault_e_indice_iguais_nao_geram_divergencia()
    {
        var estado = new[] { Estado("A.md", "um"), Estado("B.md", "dois") };
        Assert.True(Reconciliador.Comparar(estado, estado).EmDia);
    }

    [Fact]
    public void Arquivo_novo_no_disco_e_criacao()
    {
        var r = Reconciliador.Comparar([Estado("A.md", "um"), Estado("B.md", "dois")], [Estado("A.md", "um")]);
        var d = Assert.Single(r.Divergencias);
        Assert.Equal(TipoDeDivergencia.Criada, d.Tipo);
        Assert.Equal("B.md", d.Caminho.Valor);
    }

    [Fact]
    public void Arquivo_sumido_do_disco_e_remocao()
    {
        var r = Reconciliador.Comparar([Estado("A.md", "um")], [Estado("A.md", "um"), Estado("B.md", "dois")]);
        var d = Assert.Single(r.Divergencias);
        Assert.Equal(TipoDeDivergencia.Removida, d.Tipo);
        Assert.Equal("B.md", d.Caminho.Valor);
    }

    [Fact]
    public void Conteudo_diferente_e_alteracao()
    {
        var r = Reconciliador.Comparar([Estado("A.md", "novo")], [Estado("A.md", "velho")]);
        Assert.Equal(TipoDeDivergencia.Alterada, Assert.Single(r.Divergencias).Tipo);
    }

    [Fact]
    public void Data_diferente_com_mesmo_conteudo_NAO_e_alteracao()
    {
        // ESTE É O TESTE QUE PAGA A IMPRESSÃO DIGITAL. Sincronizador de nuvem e `git checkout` reescrevem
        // a data de arquivos intactos. Se a data decidisse, o vault inteiro pareceria alterado depois de
        // cada uma dessas operações e reindexaria dez mil notas à toa.
        var r = Reconciliador.Comparar([Estado("A.md", "igual", Hoje)], [Estado("A.md", "igual", Ontem)]);
        Assert.True(r.EmDia);
    }

    [Fact]
    public void Mesmo_conteudo_em_outro_caminho_e_renomeacao()
    {
        // Sem o pareamento, mover uma nota seria "apagou + criou" — e levaria junto histórico, favoritos
        // e estatísticas de estudo daquela nota.
        var r = Reconciliador.Comparar([Estado("Direito/A.md", "conteúdo")], [Estado("A.md", "conteúdo")]);
        var d = Assert.Single(r.Divergencias);
        Assert.Equal(TipoDeDivergencia.Renomeada, d.Tipo);
        Assert.Equal("A.md", d.CaminhoAnterior!.Valor);
        Assert.Equal("Direito/A.md", d.Caminho.Valor);
    }

    [Fact]
    public void Pasta_inteira_movida_vira_renomeacoes_e_nao_um_massacre()
    {
        var noIndice = new[] { Estado("A.md", "um"), Estado("B.md", "dois"), Estado("C.md", "três") };
        var noDisco = new[] { Estado("Arquivo/A.md", "um"), Estado("Arquivo/B.md", "dois"), Estado("Arquivo/C.md", "três") };

        var r = Reconciliador.Comparar(noDisco, noIndice);
        Assert.Equal(3, r.Divergencias.Count);
        Assert.All(r.Divergencias, d => Assert.Equal(TipoDeDivergencia.Renomeada, d.Tipo));
        Assert.Empty(r.Removidas);
        Assert.Empty(r.Criadas);
    }

    [Fact]
    public void Renomeacao_com_alteracao_de_conteudo_e_lida_como_remocao_mais_criacao()
    {
        // Limite CONHECIDO e aceito: mudar nome E conteúdo entre duas execuções não é rastreável sem um
        // identificador dentro do arquivo — e enfiar um id no .md quebraria a promessa de abrir no
        // Obsidian sem sujeira. Documentado aqui para que ninguém "conserte" isso por acidente.
        var r = Reconciliador.Comparar([Estado("Novo.md", "outro texto")], [Estado("Velho.md", "texto")]);
        Assert.Equal(2, r.Divergencias.Count);
        Assert.Single(r.Criadas);
        Assert.Single(r.Removidas);
    }

    [Fact]
    public void Conteudo_duplicado_pareia_de_forma_estavel()
    {
        // Duas notas idênticas trocando de pasta: não existe "o pareamento certo". O que não pode é
        // mudar entre execuções — reconciliação instável faria o histórico saltar de nota sozinho.
        var noIndice = new[] { Estado("X.md", "igual"), Estado("Y.md", "igual") };
        var noDisco = new[] { Estado("P/X.md", "igual"), Estado("P/Y.md", "igual") };

        var primeira = Reconciliador.Comparar(noDisco, noIndice);
        var segunda = Reconciliador.Comparar(noDisco.Reverse(), noIndice.Reverse());

        Assert.Equal(2, primeira.Divergencias.Count);
        Assert.All(primeira.Divergencias, d => Assert.Equal(TipoDeDivergencia.Renomeada, d.Tipo));
        Assert.Equal(primeira.Divergencias.Select(d => d.ToString()), segunda.Divergencias.Select(d => d.ToString()));
    }

    [Fact]
    public void Indice_vazio_reconstroi_tudo_e_isso_e_rotina()
    {
        // Apagar o banco e reconstruir a partir do vault tem de ser operação normal, não desastre: nada
        // que exista só no índice é conhecimento do usuário.
        var noDisco = new[] { Estado("A.md", "um"), Estado("B.md", "dois") };
        var r = Reconciliador.Comparar(noDisco, []);
        Assert.Equal(2, r.Divergencias.Count);
        Assert.All(r.Divergencias, d => Assert.Equal(TipoDeDivergencia.Criada, d.Tipo));
    }

    [Fact]
    public void Ordem_da_saida_e_deterministica()
    {
        var noDisco = new[] { Estado("Z.md", "z"), Estado("A.md", "a") };
        var r1 = Reconciliador.Comparar(noDisco, []);
        var r2 = Reconciliador.Comparar(noDisco.Reverse(), []);
        Assert.Equal(r1.Divergencias.Select(d => d.Caminho.Valor), r2.Divergencias.Select(d => d.Caminho.Valor));
        Assert.Equal("A.md", r1.Divergencias[0].Caminho.Valor);
    }

    [Fact]
    public void Quebra_de_linha_do_windows_nao_conta_como_alteracao()
    {
        // O mesmo vault sincronizado entre Windows e Linux produziria impressões diferentes para texto
        // idêntico — e o reconciliador declararia "tudo mudou" a cada troca de máquina.
        var r = Reconciliador.Comparar([Estado("A.md", "linha um\r\nlinha dois")], [Estado("A.md", "linha um\nlinha dois")]);
        Assert.True(r.EmDia);
    }
}
