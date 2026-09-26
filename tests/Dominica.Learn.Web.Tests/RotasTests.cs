using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Web;

namespace Dominica.Learn.Web.Tests;

/// <summary>
/// A regra que estes testes guardam custou um 404 em produção, numa navegação que o usuário acabou de
/// disparar: criar a nota, ser jogado numa página de erro.
///
/// Nenhuma URL interna pode começar com barra. O Blazor resolve navegação com as regras de URI, e uma
/// relativa com barra inicial é resolvida contra a RAIZ DO DOMÍNIO — descartando o sub-caminho onde o
/// app é servido. Em desenvolvimento, sem sub-caminho, as duas formas dão no mesmo e o defeito não
/// aparece; ele nasce só em produção.
/// </summary>
public class RotasTests
{
    [Theory]
    [InlineData("Crase.md")]
    [InlineData("Português/Crase.md")]
    [InlineData("Direito tributário/Direito tributário.md")]
    public void RotaDeNotaNuncaComecaComBarra(string caminho)
    {
        var rota = Rotas.Nota(caminho);

        Assert.False(rota.StartsWith('/'),
            $"'{rota}' começa com barra: sob um sub-caminho isso vira dominio.com/notas/… e dá 404.");
    }

    [Fact]
    public void RotaDeNotaEscapaEspacoEAcentoSemDestruirAsPastas()
    {
        var rota = Rotas.Nota("Direito tributário/Direito tributário.md");

        // As barras SOBREVIVEM: a rota da página é curinga (/notas/{*caminho}) e precisa delas.
        Assert.Equal("notas/Direito%20tribut%C3%A1rio/Direito%20tribut%C3%A1rio.md", rota);
    }

    [Fact]
    public void RotaDeNotaEscapaCaractereQueTeriaOutroSignificadoNaUrl()
    {
        // '#' seria âncora e '?' seria início de query — o nome viria truncado do outro lado.
        var rota = Rotas.Nota("Dúvidas? #1.md");

        Assert.DoesNotContain('#', rota);
        Assert.DoesNotContain('?', rota);
    }

    [Fact]
    public void RotaDoGrafoSemMateriaEhSoOGrafo()
    {
        Assert.Equal("grafo", Rotas.Grafo());
        Assert.Equal("grafo", Rotas.Grafo(Materia.Nenhuma));
    }

    [Fact]
    public void RotaDoGrafoComMateriaEscapaONome()
    {
        var rota = Rotas.Grafo(Materia.De("Direito tributário"));

        Assert.False(rota.StartsWith('/'));
        Assert.Equal("grafo?materia=Direito%20tribut%C3%A1rio", rota);
    }
}
