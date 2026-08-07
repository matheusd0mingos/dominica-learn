using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// A forma com que um link NASCE. O risco que estes testes guardam é o silencioso: um link ambíguo não
/// dá erro, ele leva para a nota errada — e só se descobre relendo a nota meses depois.
/// </summary>
public class EscritaDeLigacaoTests
{
    private static CaminhoNota C(string caminho) => CaminhoNota.De(caminho);

    [Fact]
    public void NomeUnicoNoVaultVaiCurto()
    {
        // O caso comum, e o que faz o texto ficar legível: quem escreve num parágrafo quer "[[Crase]]",
        // não o caminho até ela.
        var alvo = C("Português/Crase.md");

        var escrita = EscritaDeLigacao.MaisCurta(alvo, [alvo, C("Direito/Licitações.md")]);

        Assert.Equal("Crase", escrita);
    }

    [Fact]
    public void NomeRepetidoEmOutraPastaForcaOCaminho()
    {
        // Duas "Licitações" em matérias diferentes é EXATAMENTE o que acontece num vault de concurso.
        // A forma curta aqui apontaria para uma das duas ao acaso.
        var alvo = C("Direito Administrativo/Licitações.md");

        var escrita = EscritaDeLigacao.MaisCurta(alvo, [alvo, C("Direito Financeiro/Licitações.md")]);

        Assert.Equal("Direito Administrativo/Licitações", escrita);
    }

    [Fact]
    public void OCaminhoSaiSemAExtensao()
    {
        // ".md" dentro de [[ ]] funciona, mas ninguém escreve assim à mão — e o texto que o
        // autocompletar produz é lido pela pessoa todo dia.
        var alvo = C("Direito/Licitações.md");

        var escrita = EscritaDeLigacao.MaisCurta(alvo, [alvo, C("Financeiro/Licitações.md")]);

        Assert.DoesNotContain(".md", escrita);
    }

    [Fact]
    public void DiferencaDeCaixaJaContaComoHomonima()
    {
        // O resolvedor de ligações casa a forma curta sem olhar caixa. Se aqui fosse sensível, "[[Crase]]"
        // sairia como se fosse único e resolveria para a outra — ambiguidade criada pela própria
        // ferramenta que deveria evitá-la.
        var alvo = C("Português/Crase.md");

        var escrita = EscritaDeLigacao.MaisCurta(alvo, [alvo, C("Redação/crase.md")]);

        Assert.Equal("Português/Crase", escrita);
    }

    [Fact]
    public void NaRaizOCaminhoJaEhONome()
    {
        // Registra um limite honesto, não uma vitória: para uma nota na raiz, "o caminho sem extensão"
        // é o próprio nome, então a forma longa não desambigua nada. Isto NÃO é defeito — é a mesma
        // regra do Obsidian, onde o link curto resolve para a nota mais rasa. Está escrito aqui para
        // que quem ler o código não passe meia hora procurando o bug que não existe.
        var alvo = C("Licitações.md");

        var escrita = EscritaDeLigacao.MaisCurta(alvo, [alvo, C("Direito/Licitações.md")]);

        Assert.Equal("Licitações", escrita);
    }

    [Fact]
    public void VaultVazioNaoQuebra()
    {
        // Acontece de verdade: a primeira nota do vault, escrevendo um link para outra que ainda vai
        // existir. Sem homônima conhecida, o mais curto é o certo.
        var escrita = EscritaDeLigacao.MaisCurta(C("Direito/Licitações.md"), []);

        Assert.Equal("Licitações", escrita);
    }

    [Fact]
    public void AProprioAlvoNaListaNaoContaComoHomonimo()
    {
        // Contar o alvo como sua própria homônima jogaria TODO link para a forma longa. O teste existe
        // porque o defeito seria invisível: os links continuariam funcionando, só ficariam ilegíveis.
        var alvo = C("Direito/Licitações.md");

        Assert.Equal("Licitações", EscritaDeLigacao.MaisCurta(alvo, [alvo]));
    }
}
