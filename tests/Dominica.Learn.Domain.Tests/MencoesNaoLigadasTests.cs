using Dominica.Learn.Domain.Ligacoes;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// Um painel de sugestões só sobrevive se quase toda sugestão for boa. Uma menção falsa — dentro de um
/// bloco de código, no meio de outra palavra, ou apontando para algo que já está ligado — ensina a pessoa
/// a ignorar o painel, e um painel ignorado é pior que um painel que não existe. Por isso a maioria
/// destes testes é sobre o que NÃO deve aparecer.
/// </summary>
public class MencoesNaoLigadasTests
{
    [Fact]
    public void AchaONomeEscritoComoTextoComum()
    {
        var texto = "A prescrição tributária corre desde o lançamento.";

        var m = MencoesNaoLigadas.Encontrar(texto, ["prescrição"]);

        Assert.Single(m);
        Assert.Equal("prescrição", m[0].Texto);
    }

    [Fact]
    public void OQueJaEstaLigadoNaoEhMencao()
    {
        // O erro mais irritante possível: sugerir ligar o que a pessoa já ligou.
        var texto = "Ver [[Prescrição]] para o prazo.";

        Assert.Empty(MencoesNaoLigadas.Encontrar(texto, ["Prescrição"]));
    }

    [Fact]
    public void LigacaoComRotuloTambemNaoEhMencao()
    {
        // "[[Prescrição|o prazo]]" — o alvo está lá dentro, e reconhecê-lo com varredura própria de
        // colchetes é exatamente onde uma implementação improvisada erraria.
        var texto = "Ver [[Prescrição|o prazo]] adiante.";

        Assert.Empty(MencoesNaoLigadas.Encontrar(texto, ["Prescrição"]));
    }

    [Fact]
    public void PedacoDeOutraPalavraNaoConta()
    {
        // "Prescrição" dentro de "Imprescritibilidade" não é citação da nota.
        var texto = "A imprescritibilidade não se aplica aqui.";

        Assert.Empty(MencoesNaoLigadas.Encontrar(texto, ["prescri"]));
    }

    [Fact]
    public void DentroDeBlocoDeCodigoNaoConta()
    {
        var texto = "Antes.\n```\nvar prescricao = 5;\n```\nDepois.";

        Assert.Empty(MencoesNaoLigadas.Encontrar(texto, ["prescricao"]));
    }

    [Fact]
    public void DentroDeCodigoEmLinhaNaoConta()
    {
        var texto = "O campo `prescricao` guarda o prazo.";

        Assert.Empty(MencoesNaoLigadas.Encontrar(texto, ["prescricao"]));
    }

    [Fact]
    public void NoFrontmatterNaoConta()
    {
        // O frontmatter é metadado, não prosa: ligar ali quebraria o YAML.
        var texto = "---\ntitle: prescrição\n---\n\nCorpo qualquer.";

        Assert.Empty(MencoesNaoLigadas.Encontrar(texto, ["prescrição"]));
    }

    [Fact]
    public void AcentoECaixaNaoAtrapalham()
    {
        // Quem escreve corrido escreve "prescricao" sem acento e "Prescrição" no título da nota.
        var texto = "A Prescricao comeca a correr.";

        Assert.Single(MencoesNaoLigadas.Encontrar(texto, ["prescrição"]));
    }

    [Fact]
    public void APosicaoContinuaCertaDepoisDeUmAcento()
    {
        // O TESTE QUE JUSTIFICA A DOBRA DE COMPRIMENTO FIXO. Normalizar do jeito comum decompõe "ç" em
        // dois caracteres, e cada acento antes da menção deslocaria a fatia — a ligação sairia comendo a
        // letra errada. Aqui há três acentos antes do alvo.
        var texto = "Ação, coração, tradição: a prescrição corre.";

        var m = MencoesNaoLigadas.Encontrar(texto, ["prescrição"]);

        Assert.Single(m);
        Assert.Equal("prescrição", texto.Substring(m[0].Posicao, m[0].Comprimento));
    }

    [Fact]
    public void NomeCurtoDemaisEhIgnorado()
    {
        // Uma nota chamada "IR" casaria em toda página. O painel viraria ruído.
        Assert.Empty(MencoesNaoLigadas.Encontrar("O IR incide sobre a renda.", ["IR"]));
    }

    [Fact]
    public void ApelidoTambemEhProcurado()
    {
        // "CF/88" no texto é citação da nota "Constituição Federal". Sem os apelidos, ficariam de fora
        // justamente as menções que ninguém lembraria de procurar.
        var texto = "A CF/88 trata disso no artigo 150.";

        var m = MencoesNaoLigadas.Encontrar(texto, ["Constituição Federal", "CF/88"]);

        Assert.Single(m);
        Assert.Equal("CF/88", m[0].Texto);
    }

    [Fact]
    public void DuasMencoesNaMesmaNotaAparecemAsDuas()
    {
        var texto = "A prescrição corre.\nMas a prescrição pode ser interrompida.";

        Assert.Equal(2, MencoesNaoLigadas.Encontrar(texto, ["prescrição"]).Count);
    }

    [Fact]
    public void NomeEApelidoNoMesmoLugarNaoViramDuasMencoes()
    {
        // Ligar duas vezes a mesma fatia corromperia o texto.
        var texto = "A prescrição corre.";

        Assert.Single(MencoesNaoLigadas.Encontrar(texto, ["prescrição", "Prescrição"]));
    }

    [Fact]
    public void LigarTrocaSoAFatiaDaMencao()
    {
        var texto = "A prescrição corre desde o lançamento.";
        var m = MencoesNaoLigadas.Encontrar(texto, ["Prescrição"])[0];

        var novo = MencoesNaoLigadas.Ligar(texto, m, "Prescrição");

        Assert.Equal("A [[Prescrição|prescrição]] corre desde o lançamento.", novo);
    }

    [Fact]
    public void LigarPreservaOTextoQuandoEleJaBateComOAlvo()
    {
        // Sem rótulo quando não é preciso: "[[Prescrição|Prescrição]]" é ruído no texto que a pessoa lê.
        var texto = "Prescrição corre desde o lançamento.";
        var m = MencoesNaoLigadas.Encontrar(texto, ["Prescrição"])[0];

        Assert.Equal("[[Prescrição]] corre desde o lançamento.", MencoesNaoLigadas.Ligar(texto, m, "Prescrição"));
    }

    [Fact]
    public void LigarRecusaQuandoOTextoMudouEmbaixo()
    {
        // A nota pode ter sido editada entre a tela listar a menção e a pessoa clicar. Gravar por cima da
        // posição antiga estragaria uma frase qualquer, sem avisar.
        var texto = "A prescrição corre.";
        var m = MencoesNaoLigadas.Encontrar(texto, ["Prescrição"])[0];

        Assert.Null(MencoesNaoLigadas.Ligar("Outro texto completamente diferente aqui.", m, "Prescrição"));
    }

    [Fact]
    public void SemNomeNenhumNaoAchaNada()
    {
        Assert.Empty(MencoesNaoLigadas.Encontrar("Qualquer texto.", []));
        Assert.Empty(MencoesNaoLigadas.Encontrar("Qualquer texto.", ["", "  "]));
    }
}
