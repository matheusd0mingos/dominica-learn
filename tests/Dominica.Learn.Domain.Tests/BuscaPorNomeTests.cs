using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// Um abridor rápido só substitui a lista se acertar com POUCAS TECLAS e puser o certo em PRIMEIRO.
/// Se o resultado certo aparece em quinto, a pessoa volta a rolar a lista e a ferramenta morre. Por
/// isso quase todo teste aqui é sobre ORDEM, não sobre filtragem.
/// </summary>
public class BuscaPorNomeTests
{
    private static string Primeiro(string termo, params string[] candidatos) =>
        BuscaPorNome.Ordenar(termo, candidatos)[0].Texto;

    [Fact]
    public void CasaLetrasNaOrdemComBuracosNoMeio()
    {
        // "dirtrib" tem de achar "Direito tributário" — é o ponto todo de não ser um Contains.
        var r = BuscaPorNome.Ordenar("dirtrib", ["Direito tributário/Direito tributário.md", "Português/Crase.md"]);

        Assert.Single(r);
        Assert.Contains("Direito tributário", r[0].Texto);
    }

    [Fact]
    public void FaltandoLetraNaoEhCandidato()
    {
        var r = BuscaPorNome.Ordenar("xyz", ["Direito/Licitações.md", "Português/Crase.md"]);

        Assert.Empty(r);
    }

    [Fact]
    public void AcentoNaoAtrapalha()
    {
        // Exigir o acento é exigir que a pessoa pare para lembrar da grafia bem quando está com pressa.
        var r = BuscaPorNome.Ordenar("portugues", ["Português/Crase.md"]);

        Assert.Single(r);
    }

    [Fact]
    public void BuscaAcentuadaTambemAcha()
    {
        var r = BuscaPorNome.Ordenar("licitações", ["Direito/Licitações.md"]);

        Assert.Single(r);
    }

    [Fact]
    public void NomeExatoGanhaDoNomeMaisLongo()
    {
        // Quem digita "crase" quase sempre quer "Crase", não "Crase e acentuação".
        var vencedor = Primeiro("crase", "Português/Crase e acentuação.md", "Português/Crase.md");

        Assert.Equal("Português/Crase.md", vencedor);
    }

    [Fact]
    public void ONomeDoArquivoGanhaDaPasta()
    {
        // Sem este peso, uma PASTA chamada "Licitações" empurraria a NOTA "Licitações" para baixo.
        var vencedor = Primeiro("licit", "Licitações/Modalidades.md", "Direito/Licitações.md");

        Assert.Equal("Direito/Licitações.md", vencedor);
    }

    [Fact]
    public void ComecoDePalavraGanhaDoMeioDaPalavra()
    {
        var vencedor = Primeiro("ct", "Direito/Contratos.md", "Português/Acentuação.md");

        Assert.Equal("Direito/Contratos.md", vencedor);
    }

    [Fact]
    public void LetrasColadasGanhamDeLetrasEspalhadas()
    {
        var vencedor = Primeiro("cont", "Direito/Contratos.md", "Direito/Concurso de normas técnicas.md");

        Assert.Equal("Direito/Contratos.md", vencedor);
    }

    [Fact]
    public void TermoVazioDevolveTudoNaOrdemQueVeio()
    {
        // É o estado de quem abriu o buscador e ainda não digitou: ali a ordem do chamador
        // (recentes primeiro) é a melhor informação que existe.
        string[] entrada = ["b.md", "a.md", "c.md"];

        var r = BuscaPorNome.Ordenar("", entrada);

        Assert.Equal(entrada, r.Select(x => x.Texto));
    }

    [Fact]
    public void EspacoEmVoltaDoTermoNaoMudaNada()
    {
        Assert.Single(BuscaPorNome.Ordenar("  crase  ", ["Português/Crase.md"]));
    }

    [Fact]
    public void ListaVaziaNaoQuebra()
    {
        Assert.Empty(BuscaPorNome.Ordenar("qualquer", []));
    }

    [Fact]
    public void DevolveAsPosicoesQueCasaramParaATelaDestacar()
    {
        var r = BuscaPorNome.Ordenar("cr", ["Crase.md"]);

        Assert.Equal([0, 1], r[0].Posicoes);
    }

    [Fact]
    public void ANotaIndiceDaMateriaVemAntesDasNotasDaPasta()
    {
        // O caso que este produto CRIA sozinho: toda matéria nasce como pasta + nota de mesmo nome.
        // Com o casamento guloso, "portug" era consumido inteiro pela PASTA de "Português/Origem.md",
        // o bônus de nome de arquivo nunca disparava, e a nota-índice caía para baixo. Quem digita o
        // nome da matéria quer a matéria.
        var r = BuscaPorNome.Ordenar("portug",
            ["Português/Origem.md", "Português/Português.md", "Português/Crase.md"]);

        Assert.Equal("Português/Português.md", r[0].Texto);
    }

    [Fact]
    public void ONomeDoArquivoContinuaGanhandoDaPasta()
    {
        // A regra antiga, que a segunda passada não pode ter estragado.
        var r = BuscaPorNome.Ordenar("licit", ["Licitações/Prazos.md", "Direito/Licitações.md"]);

        Assert.Equal("Direito/Licitações.md", r[0].Texto);
    }

    // —— O CRIVO CONTÍGUO DO COMPLETAR DE LIGAÇÃO ————————————————————————————————————
    // Aceitar sugestão de "[[" escreve no arquivo: o crivo é mais duro que a busca solta de propósito.

    [Fact]
    public void Subsequencia_com_buraco_NAO_passa_no_crivo_contiguo()
    {
        // O caso real que motivou o crivo: "deca" casava por subsequência com "…cai DE Contabilidade"
        // e o Enter linkava a nota errada.
        Assert.False(BuscaPorNome.ContemTrecho("deca", "Lei 6.404 — o que cai de contabilidade.md"));
        Assert.True(BuscaPorNome.ContemTrecho("deca", "Tributário/Decadência.md"));
    }

    [Fact]
    public void O_crivo_ignora_acento_e_caixa()
    {
        Assert.True(BuscaPorNome.ContemTrecho("prescricao", "Direito/Prescrição.md"));
        Assert.True(BuscaPorNome.ContemTrecho("LICIT", "Direito/Licitações.md"));
    }

    [Fact]
    public void Termo_vazio_passa_e_e_o_estado_da_lista_de_recentes()
    {
        Assert.True(BuscaPorNome.ContemTrecho("", "Qualquer.md"));
        Assert.True(BuscaPorNome.ContemTrecho(null, "Qualquer.md"));
    }
}
