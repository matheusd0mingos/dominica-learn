using Dominica.Learn.Domain.Estudo;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O ciclo decide o que a pessoa estuda agora. Um erro aqui manda estudar a matéria errada, ou trava a
/// volta num bloco que já foi feito — o tipo de defeito que quebra a confiança no rodízio.
/// </summary>
public class CicloDeEstudosTests
{
    [Fact]
    public void Le_a_lista_de_tarefas_como_blocos()
    {
        var conteudo = "# Ciclo de estudos\n\n- [ ] Direito · 50 min\n- [x] Português · 30 min\n";
        var blocos = CicloDeEstudos.Ler(conteudo);

        Assert.Equal(2, blocos.Count);
        Assert.Equal("Direito", blocos[0].Materia.Nome);
        Assert.Equal(50, blocos[0].Minutos);
        Assert.False(blocos[0].Concluido);
        Assert.True(blocos[1].Concluido);
    }

    [Fact]
    public void Texto_solto_em_volta_da_lista_e_ignorado()
    {
        // A pessoa pode anotar o que quiser na nota; o ciclo só enxerga os itens de tarefa.
        var conteudo = "# Meu ciclo\n\nfoco na reta final\n\n- [ ] Direito · 50 min\n\nrevisar à noite\n";
        Assert.Single(CicloDeEstudos.Ler(conteudo));
    }

    [Fact]
    public void Sem_o_ponto_o_bloco_usa_os_minutos_padrao()
    {
        var blocos = CicloDeEstudos.Ler("- [ ] Direito\n");
        Assert.Equal(CicloDeEstudos.MinutosPadrao, Assert.Single(blocos).Minutos);
    }

    [Fact]
    public void Ler_e_escrever_dao_a_volta_completa()
    {
        var blocos = new List<BlocoDoCiclo>
        {
            new(Materia.De("Direito"), 50, false),
            new(Materia.De("Português"), 30, true),
        };
        Assert.Equal(blocos, CicloDeEstudos.Ler(CicloDeEstudos.Escrever(blocos)));
    }

    [Fact]
    public void O_atual_e_o_primeiro_pendente()
    {
        var blocos = CicloDeEstudos.Ler("- [x] Direito · 50 min\n- [ ] Português · 30 min\n- [ ] Direito · 50 min\n");
        Assert.Equal(1, CicloDeEstudos.IndiceAtual(blocos));
    }

    [Fact]
    public void Concluir_o_atual_avanca_para_o_proximo()
    {
        var blocos = CicloDeEstudos.Ler("- [ ] Direito · 50 min\n- [ ] Português · 30 min\n");
        var depois = CicloDeEstudos.Concluir(blocos, 0);

        Assert.True(depois[0].Concluido);
        Assert.Equal(1, CicloDeEstudos.IndiceAtual(depois));
    }

    [Fact]
    public void Concluir_o_ultimo_ZERA_a_volta_e_recomeca_do_inicio()
    {
        // "Girar o ciclo": terminou a volta, ela recomeça sozinha — sem deixar um ciclo todo riscado.
        var blocos = CicloDeEstudos.Ler("- [x] Direito · 50 min\n- [ ] Português · 30 min\n");
        var depois = CicloDeEstudos.Concluir(blocos, 1);

        Assert.All(depois, b => Assert.False(b.Concluido));
        Assert.Equal(0, CicloDeEstudos.IndiceAtual(depois));
    }

    [Fact]
    public void Ciclo_vazio_nao_tem_atual()
    {
        Assert.Null(CicloDeEstudos.IndiceAtual([]));
        Assert.Empty(CicloDeEstudos.Ler(""));
    }

    [Fact]
    public void O_peso_e_a_frequencia_na_fila()
    {
        // "Por peso" = a matéria mais pesada aparece mais vezes. Direito 2×, Português 1×: o ciclo
        // entrega Direito, Português, Direito — mais Direito por volta, que é o que peso maior quer dizer.
        var blocos = CicloDeEstudos.Ler("- [ ] Direito · 50 min\n- [ ] Português · 30 min\n- [ ] Direito · 50 min\n");
        Assert.Equal(2, blocos.Count(b => b.Materia.Nome == "Direito"));
        Assert.Equal(1, blocos.Count(b => b.Materia.Nome == "Português"));
    }
}
