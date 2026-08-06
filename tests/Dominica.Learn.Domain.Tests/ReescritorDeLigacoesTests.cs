using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// Renomear sem consertar os links é a forma mais silenciosa de perder conhecimento: nada some do
/// disco, mas o caminho até ele some. Estes testes existem para que essa regra não se perca numa
/// refatoração — cada um deles é um jeito de a ligação quebrar sem ninguém notar.
/// </summary>
public class ReescritorDeLigacoesTests
{
    private static CaminhoNota C(string v) => CaminhoNota.De(v);

    /// <summary>
    /// Um vault de mentira: o resolvedor que o caso de uso monta a partir do índice. Aqui ele é uma
    /// lista, e resolve tanto pelo nome curto quanto pelo caminho — como o Obsidian faz.
    /// </summary>
    private static Func<string, CaminhoNota?> Vault(params string[] caminhos)
    {
        var notas = caminhos.Select(C).ToList();
        return alvo =>
        {
            var limpo = alvo.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? alvo[..^3] : alvo;
            return notas.FirstOrDefault(n => n.Valor.Equals(limpo + ".md", StringComparison.Ordinal))
                ?? notas.FirstOrDefault(n => n.Nome.Equals(limpo, StringComparison.Ordinal));
        };
    }

    [Fact]
    public void MudarDePastaNaoMexeNoLinkCurto()
    {
        // A forma curta continua resolvendo para a nota certa — mexer nela poluiria o texto à toa.
        var texto = "Ver [[Licitações]] antes da prova.";
        var vault = Vault("Direito/Licitações.md");

        var saida = ReescritorDeLigacoes.Reescrever(
            texto, C("Direito/Licitações.md"), C("Direito Administrativo/Licitações.md"), vault);

        Assert.Same(texto, saida);   // MESMA instância: o chamador não regrava o arquivo
    }

    [Fact]
    public void MudarONomeReescreveOLinkCurto()
    {
        var vault = Vault("Direito/Licitações.md");

        var saida = ReescritorDeLigacoes.Reescrever(
            "Ver [[Licitações]] antes da prova.",
            C("Direito/Licitações.md"), C("Direito/Licitações e contratos.md"), vault);

        Assert.Equal("Ver [[Licitações e contratos]] antes da prova.", saida);
    }

    [Fact]
    public void LinkEscritoComPastaAcompanhaAPasta()
    {
        var vault = Vault("Direito/Licitações.md");

        var saida = ReescritorDeLigacoes.Reescrever(
            "Ver [[Direito/Licitações]].",
            C("Direito/Licitações.md"), C("Direito Administrativo/Licitações.md"), vault);

        Assert.Equal("Ver [[Direito Administrativo/Licitações]].", saida);
    }

    [Fact]
    public void SecaoERotuloSobrevivem()
    {
        var vault = Vault("Direito/Licitações.md");

        var saida = ReescritorDeLigacoes.Reescrever(
            "Ver [[Licitações#Modalidades|as modalidades]].",
            C("Direito/Licitações.md"), C("Direito/Certames.md"), vault);

        Assert.Equal("Ver [[Certames#Modalidades|as modalidades]].", saida);
    }

    [Fact]
    public void EmbedContinuaEmbed()
    {
        var vault = Vault("Direito/Licitações.md");

        var saida = ReescritorDeLigacoes.Reescrever(
            "![[Licitações]]", C("Direito/Licitações.md"), C("Direito/Certames.md"), vault);

        // Sem o "!", o conteúdo deixaria de ser embutido e viraria só um link — a nota mudaria de cara.
        Assert.Equal("![[Certames]]", saida);
    }

    [Fact]
    public void LinkMarkdownMantemOFormatoEEscapaOEspaco()
    {
        var vault = Vault("Direito/Licitações.md");

        var saida = ReescritorDeLigacoes.Reescrever(
            "Ver [as licitações](Direito/Licitações.md).",
            C("Direito/Licitações.md"), C("Direito Administrativo/Licitações.md"), vault);

        // Espaço cru fecharia o parêntese cedo e o resto do caminho viraria "título" do link.
        Assert.Equal("Ver [as licitações](Direito%20Administrativo/Licitações.md).", saida);
    }

    [Fact]
    public void VariasLigacoesNaMesmaNotaSaoTodasReescritas()
    {
        var vault = Vault("Direito/Licitações.md");

        var saida = ReescritorDeLigacoes.Reescrever(
            "[[Licitações]] e depois [[Licitações#Fases]] e ainda [[Licitações|elas]].",
            C("Direito/Licitações.md"), C("Direito/Certames.md"), vault);

        Assert.Equal("[[Certames]] e depois [[Certames#Fases]] e ainda [[Certames|elas]].", saida);
        Assert.DoesNotContain("Licitações", saida);
    }

    [Fact]
    public void LigacaoParaOUTRANotaNaoEhTocada()
    {
        var vault = Vault("Direito/Licitações.md", "Direito/Contratos.md");

        var texto = "[[Contratos]] continua igual, [[Licitações]] muda.";
        var saida = ReescritorDeLigacoes.Reescrever(
            texto, C("Direito/Licitações.md"), C("Direito/Certames.md"), vault);

        Assert.Equal("[[Contratos]] continua igual, [[Certames]] muda.", saida);
    }

    [Fact]
    public void LinkDentroDeCodigoNaoEhReescrito()
    {
        // O analisador ignora o que está entre crases; um exemplo de sintaxe numa nota de estudo não é
        // uma ligação de verdade e reescrevê-lo estragaria a explicação.
        var vault = Vault("Direito/Licitações.md");

        var texto = "Escreva `[[Licitações]]` para ligar.";
        var saida = ReescritorDeLigacoes.Reescrever(
            texto, C("Direito/Licitações.md"), C("Direito/Certames.md"), vault);

        Assert.Same(texto, saida);
    }

    [Fact]
    public void LinkExternoNaoEhTocado()
    {
        var vault = Vault("Direito/Licitações.md");

        var texto = "Fonte: [lei](https://planalto.gov.br/Licitações).";
        var saida = ReescritorDeLigacoes.Reescrever(
            texto, C("Direito/Licitações.md"), C("Direito/Certames.md"), vault);

        Assert.Same(texto, saida);
    }

    [Fact]
    public void NomeNovoAMBIGUOForcaOCaminhoCompleto()
    {
        // Já existe OUTRA nota chamada "Certames" em outra pasta. Manter a forma curta faria a ligação
        // passar a apontar para a nota errada — que é pior que um link quebrado, porque parece certo.
        var vault = Vault("Direito/Licitações.md", "Português/Certames.md");

        var saida = ReescritorDeLigacoes.Reescrever(
            "Ver [[Licitações]].", C("Direito/Licitações.md"), C("Direito/Certames.md"), vault);

        Assert.Equal("Ver [[Direito/Certames]].", saida);
    }

    [Fact]
    public void ContarDizQuantasLigacoesApontamParaANota()
    {
        var vault = Vault("Direito/Licitações.md");

        var quantas = ReescritorDeLigacoes.Contar(
            "[[Licitações]], [[Licitações#Fases]] e `[[Licitações]]` em código.",
            C("Direito/Licitações.md"), vault);

        Assert.Equal(2, quantas);
    }

    [Fact]
    public void RenomearParaOMesmoCaminhoNaoMexeEmNada()
    {
        var texto = "[[Licitações]]";
        var saida = ReescritorDeLigacoes.Reescrever(
            texto, C("Direito/Licitações.md"), C("Direito/Licitações.md"), Vault("Direito/Licitações.md"));

        Assert.Same(texto, saida);
    }
}
