using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// Um zip enviado por usuário é a entrada mais hostil que este sistema aceita. Cada teste aqui é um
/// ataque conhecido — e o dia em que um deles ficar vermelho é o dia em que alguém escreve fora do
/// vault, ou enche o disco do servidor e derruba o vault de todo mundo.
/// </summary>
public class RegrasDeImportacaoTests
{
    private const string Chegada = "Importado 2026-08-07";

    private static IReadOnlyList<EntradaJulgada> Julgar(params (string, long)[] entradas) =>
        RegrasDeImportacao.Julgar(entradas, Chegada);

    // —— ZIP SLIP ————————————————————————————————————————————————————————————————————
    [Theory]
    [InlineData("../fora.md")]
    [InlineData("../../etc/senha.md")]
    [InlineData("pasta/../../fora.md")]
    [InlineData("..\\..\\fora.md")]              // barra invertida é a MESMA ameaça
    [InlineData("/etc/passwd.md")]               // caminho absoluto unix
    [InlineData("C:/Windows/algo.md")]           // caminho absoluto windows
    public void CaminhoQueTentaEscaparEhRecusado(string nome)
    {
        var r = Julgar((nome, 100))[0];

        Assert.False(r.Aceita);
        Assert.Equal(MotivoDaRecusa.CaminhoEscapa, r.Recusa);
    }

    [Fact]
    public void NotaNormalEhAceitaDentroDaPastaDeChegada()
    {
        var r = Julgar(("Direito/Licitações.md", 2048))[0];

        Assert.True(r.Aceita);
        Assert.Equal($"{Chegada}/Direito/Licitações.md", r.Destino);
    }

    [Fact]
    public void NadaAterrissaNaRaizDoVault()
    {
        // A garantia que faz "importar" não poder sobrescrever nada: TUDO cai na pasta carimbada.
        var julgadas = Julgar(("a.md", 10), ("b/c.md", 10), ("img.png", 10));

        Assert.All(julgadas.Where(j => j.Aceita),
            j => Assert.StartsWith(Chegada + "/", j.Destino!, StringComparison.Ordinal));
    }

    // —— ZIP BOMB ————————————————————————————————————————————————————————————————————
    [Fact]
    public void PacoteQuePassaDoTamanhoTotalEhCortado()
    {
        // 1 MB que vira 40 GB é o ataque; aqui o teto é conferido ANTES de gravar qualquer coisa.
        //
        // São ANEXOS e não notas de propósito: uma nota de 256 MB já cairia antes, no teto por
        // arquivo. O que este teste guarda é o teto do PACOTE, e ele precisa de entradas que passem
        // pelo teto individual — senão ele passaria a verde pelo motivo errado, que é a pior
        // categoria de teste. (Foi o que aconteceu quando escrevi ".md" aqui.)
        var metade = RegrasDeImportacao.TamanhoMaximoDescompactadoBytes / 2 + 1;

        var julgadas = Julgar(("a.png", metade), ("b.png", metade), ("c.md", 100));

        Assert.True(julgadas[0].Aceita);
        Assert.Equal(MotivoDaRecusa.PacoteGrandeDemais, julgadas[1].Recusa);
    }

    [Fact]
    public void ArquivoUnicoAbsurdoParaUmaNotaEhRecusado()
    {
        var r = Julgar(("gigante.md", RegrasDeImportacao.TamanhoMaximoDeNotaBytes + 1))[0];

        Assert.Equal(MotivoDaRecusa.ArquivoGrandeDemais, r.Recusa);
    }

    [Fact]
    public void PacoteComEntradasDemaisEhCortado()
    {
        var muitas = Enumerable
            .Range(0, RegrasDeImportacao.MaximoDeEntradas + 5)
            .Select(i => ($"n{i}.md", 10L));

        var julgadas = RegrasDeImportacao.Julgar(muitas, Chegada);

        Assert.Equal(MotivoDaRecusa.PacoteGrandeDemais, julgadas[^1].Recusa);
    }

    // —— TIPO ————————————————————————————————————————————————————————————————————————
    [Fact]
    public void SvgEhRecusadoMesmoParecendoImagem()
    {
        // SVG é XML que executa script, e o vault é renderizado no navegador de quem o abre.
        var r = Julgar(("Anexos/desenho.svg", 500))[0];

        Assert.Equal(MotivoDaRecusa.TipoNaoPermitido, r.Recusa);
    }

    [Theory]
    [InlineData("script.js")]
    [InlineData("programa.exe")]
    [InlineData("planilha.xlsx")]
    [InlineData("arquivo.sh")]
    public void TipoForaDaListaEhRecusado(string nome)
    {
        Assert.Equal(MotivoDaRecusa.TipoNaoPermitido, Julgar((nome, 100))[0].Recusa);
    }

    [Theory]
    [InlineData("Anexos/foto.png")]
    [InlineData("Anexos/leitura.pdf")]
    [InlineData("Anexos/aula.mp3")]
    public void AnexoDeTipoPermitidoPassa(string nome)
    {
        Assert.True(Julgar((nome, 1000))[0].Aceita);
    }

    // —— BORDAS ——————————————————————————————————————————————————————————————————————
    [Fact]
    public void EntradaDeDiretorioNaoEhErroNemArquivo()
    {
        var r = Julgar(("Direito/", 0))[0];

        Assert.False(r.Aceita);
        Assert.Equal(MotivoDaRecusa.NomeInvalido, r.Recusa);
    }

    [Fact]
    public void NomeVazioNaoQuebra()
    {
        Assert.Equal(MotivoDaRecusa.NomeInvalido, Julgar(("", 0))[0].Recusa);
    }

    [Fact]
    public void PacoteVazioDevolveListaVazia()
    {
        Assert.Empty(RegrasDeImportacao.Julgar([], Chegada));
    }

    [Fact]
    public void AOrdemEPreservadaParaOUsuarioEntenderORelatorio()
    {
        var julgadas = Julgar(("a.md", 10), ("../x.md", 10), ("b.md", 10));

        Assert.Equal(["a.md", "../x.md", "b.md"], julgadas.Select(j => j.NomeNoPacote));
    }

    [Fact]
    public void PastaDeChegadaCarregaADataParaNaoColidirComOutroImport()
    {
        var a = RegrasDeImportacao.PastaDeChegada(new DateOnly(2026, 8, 7));
        var b = RegrasDeImportacao.PastaDeChegada(new DateOnly(2026, 8, 8));

        Assert.NotEqual(a, b);
        Assert.Equal("Importado 2026-08-07", a);
    }
}
