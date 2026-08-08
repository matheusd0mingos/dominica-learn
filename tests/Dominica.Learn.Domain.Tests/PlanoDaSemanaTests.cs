using Dominica.Learn.Domain.Estudo;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O plano da semana propõe onde botar as horas. Um erro aqui manda estudar o forte e abandonar o fraco —
/// o oposto do que o loop de fraqueza existe para fazer — ou recusa um plano que a pessoa escreveu à mão.
/// </summary>
public class PlanoDaSemanaTests
{
    private static readonly Materia Tributario = Materia.De("Tributário");
    private static readonly Materia Direito = Materia.De("Direito");
    private static readonly Materia Portugues = Materia.De("Português");

    // —— LER E ESCREVER ——————————————————————————————————————————————————————————————

    [Fact]
    public void Le_a_lista_de_metas()
    {
        var metas = PlanoDaSemana.Ler("# Plano da semana\n\n- Tributário: 5h\n- Direito: 1h30\n");

        Assert.Equal(2, metas.Count);
        Assert.Equal("Tributário", metas[0].Materia.Nome);
        Assert.Equal(TimeSpan.FromHours(5), metas[0].Meta);
        Assert.Equal(TimeSpan.FromMinutes(90), metas[1].Meta);
    }

    [Fact]
    public void Texto_solto_e_ignorado()
    {
        var metas = PlanoDaSemana.Ler("# Plano\n\nfoco em tributário\n\n- Direito: 2h\n\nresto à noite\n");
        Assert.Single(metas);
    }

    [Fact]
    public void Ler_e_escrever_dao_a_volta_completa()
    {
        var metas = new List<MetaDaSemana>
        {
            new(Tributario, TimeSpan.FromHours(5)),
            new(Direito, TimeSpan.FromMinutes(90)),
        };
        Assert.Equal(metas, PlanoDaSemana.Ler(PlanoDaSemana.Escrever(metas)));
    }

    [Theory]
    [InlineData("5h", 300)]
    [InlineData("1h30", 90)]
    [InlineData("1h30min", 90)]
    [InlineData("90min", 90)]
    [InlineData("45 min", 45)]
    [InlineData("2", 120)]   // só o número = horas
    public void Le_a_duracao_em_varios_formatos(string texto, int minutosEsperados)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutosEsperados), PlanoDaSemana.LerDuracao(texto));
    }

    [Fact]
    public void Plano_vazio_e_lista_vazia()
    {
        Assert.Empty(PlanoDaSemana.Ler(""));
        Assert.Empty(PlanoDaSemana.Ler(null));
    }

    // —— PROPOR PELA FRAQUEZA ————————————————————————————————————————————————————————

    [Fact]
    public void A_materia_mais_fraca_ganha_mais_horas()
    {
        // Tributário 40% (erra 60), Direito 80% (erra 20): Tributário tem que vir com mais horas.
        var metas = PlanoDaSemana.Propor(
            [new(Tributario, 40), new(Direito, 80)], TimeSpan.FromHours(10));

        Assert.Equal(Tributario, metas[0].Materia);              // a mais fraca no topo
        Assert.True(metas[0].Meta > metas[1].Meta, "a mais fraca devia ganhar mais horas");
    }

    [Fact]
    public void A_materia_forte_nao_some_do_plano_ganha_o_minimo()
    {
        // Direito com 100% de acerto: mesmo sem erro, mantém uma fatia — manter o que se sabe é estudo.
        var metas = PlanoDaSemana.Propor(
            [new(Tributario, 30), new(Direito, 100)], TimeSpan.FromHours(10));

        var direito = metas.Single(m => m.Materia == Direito);
        Assert.True(direito.Meta >= PlanoDaSemana.MinimoPorMateria);
    }

    [Fact]
    public void Sem_amostra_conta_como_fraqueza_media_e_entra_no_plano()
    {
        // Matéria nunca testada (acerto nulo) não é ignorada: entra com peso médio.
        var metas = PlanoDaSemana.Propor([new(Tributario, null)], TimeSpan.FromHours(4));
        Assert.Single(metas);
        Assert.True(metas[0].Meta > TimeSpan.Zero);
    }

    [Fact]
    public void As_metas_sao_arredondadas_para_meia_hora()
    {
        var metas = PlanoDaSemana.Propor(
            [new(Tributario, 55), new(Direito, 70), new(Portugues, 35)], TimeSpan.FromHours(9));

        Assert.All(metas, m => Assert.Equal(0, m.Meta.TotalMinutes % 30));
    }

    [Fact]
    public void Sem_candidatas_ou_sem_orcamento_nao_propoe_nada()
    {
        Assert.Empty(PlanoDaSemana.Propor([], TimeSpan.FromHours(10)));
        Assert.Empty(PlanoDaSemana.Propor([new(Tributario, 40)], TimeSpan.Zero));
    }

    [Fact]
    public void O_orcamento_maior_gera_metas_maiores()
    {
        var candidatas = new[] { new CandidataAoPlano(Tributario, 40), new CandidataAoPlano(Direito, 80) };
        var pequeno = PlanoDaSemana.Propor(candidatas, TimeSpan.FromHours(6));
        var grande = PlanoDaSemana.Propor(candidatas, TimeSpan.FromHours(20));

        Assert.True(grande.Sum(m => m.Meta.TotalMinutes) > pequeno.Sum(m => m.Meta.TotalMinutes));
    }
}
