using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O TOKEN DE UM LINK PÚBLICO — que aqui, ao contrário do código de turma, VALE COMO SENHA.
///
/// Do outro lado do link não há login: quem tem o token lê a nota. Então o que este arquivo guarda são
/// as três propriedades de que a segurança do recurso depende, e nenhuma delas dá erro quando quebra —
/// todas passam a "funcionar" e só ficam frágeis.
/// </summary>
public class TokenDeLinkTests
{
    [Fact]
    public void Sorteia_no_tamanho_e_no_alfabeto_declarados()
    {
        var token = TokenDeLink.Sortear();

        Assert.Equal(TokenDeLink.Tamanho, token.Valor.Length);
        Assert.All(token.Valor, c => Assert.True(char.IsAsciiLetterOrDigit(c), $"caractere fora do alfabeto: {c}"));
    }

    /// <summary>
    /// NÃO REPETE. É a propriedade que faz o token ser segredo, e o teste que pegaria a regressão mais
    /// provável de todas: alguém trocar o sorteio criptográfico por um <c>Random</c> compartilhado, ou
    /// por um contador. Mil sorteios não provam imprevisibilidade — nenhum teste prova — mas provam que
    /// não é constante nem sequencial, que é como esse erro aparece na prática.
    /// </summary>
    [Fact]
    public void Dois_sorteios_nunca_dao_o_mesmo_token()
    {
        var vistos = new HashSet<string>();
        for (var i = 0; i < 1_000; i++)
            Assert.True(vistos.Add(TokenDeLink.Sortear().Valor), "sorteou um token repetido");
    }

    /// <summary>
    /// A ENTROPIA É A DEFESA INTEIRA, e por isso ela está escrita como asserção e não só no comentário:
    /// encurtar o token para "ficar mais bonito na URL" é a mudança inocente que ninguém liga a
    /// segurança. Com 62 caracteres possíveis, 22 posições dão mais de 120 bits.
    /// </summary>
    [Fact]
    public void O_token_tem_folga_de_entropia_para_valer_como_segredo()
    {
        var bits = TokenDeLink.Tamanho * Math.Log2(62);
        Assert.True(bits >= 120, $"o token caiu para ~{bits:F0} bits — adivinhar deixa de ser impossível");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curto")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]                  // longo demais
    [InlineData("aaaaaaaaaaaaaaaaaaaaa-")]                          // hífen não está no alfabeto
    [InlineData("aaaaaaaaaaaaaaaaaaaa/.")]                          // e nem barra ou ponto
    public void Recusa_o_que_nao_tem_a_forma_de_token(string? bruto)
    {
        Assert.False(TokenDeLink.TentarCriar(bruto, out var token));
        Assert.Null(token);
    }

    [Fact]
    public void Aceita_de_volta_o_que_ele_mesmo_sorteou()
    {
        var sorteado = TokenDeLink.Sortear();

        Assert.True(TokenDeLink.TentarCriar(sorteado.Valor, out var lido));
        Assert.Equal(sorteado, lido);
    }

    /// <summary>
    /// A CAIXA IMPORTA — e este é o teste do erro tentador. O código de turma normaliza para maiúsculas
    /// porque é ditado por voz; copiar essa gentileza para cá colapsaria "aB…" e "Ab…" num token só,
    /// dividindo o espaço de busca pela metade a cada letra. Não daria erro em lugar nenhum: os links
    /// continuariam abrindo.
    /// </summary>
    [Fact]
    public void Nao_normaliza_a_caixa_como_o_codigo_de_turma_faz()
    {
        var comMaiuscula = new string('A', TokenDeLink.Tamanho);
        var comMinuscula = new string('a', TokenDeLink.Tamanho);

        Assert.True(TokenDeLink.TentarCriar(comMaiuscula, out var um));
        Assert.True(TokenDeLink.TentarCriar(comMinuscula, out var outro));
        Assert.NotEqual(um, outro);
    }

    /// <summary>Espaço em volta também não é aparado: " tok…" e "tok…" são endereços diferentes, e um deles não existe.</summary>
    [Fact]
    public void Nao_apara_espaco()
    {
        var sorteado = TokenDeLink.Sortear();
        Assert.False(TokenDeLink.TentarCriar(" " + sorteado.Valor, out _));
    }
}

/// <summary>A linha que liga um token a uma nota de alguém.</summary>
public class LinkDeNotaTests
{
    private static CaminhoNota Caminho(string v)
    {
        CaminhoNota.TentarCriar(v, out var c, out _);
        return c!;
    }

    [Fact]
    public void Monta_com_todas_as_pecas()
    {
        var link = LinkDeNota.TentarCriar(
            TokenDeLink.Sortear(), ApelidoDoUsuario.De("matheus"), NomeDoVault.De("estudo"),
            Caminho("Contabilidade/Aula01.md"), DateTimeOffset.UnixEpoch);

        Assert.NotNull(link);
        Assert.Equal("matheus", link.Dono.Valor);
        Assert.Equal("estudo", link.Vault.Valor);
    }

    /// <summary>
    /// FALTANDO UMA PEÇA, NÃO EXISTE LINK. Uma linha com dono nulo não é "link ruim": é uma autorização
    /// sem sujeito, e uma consulta descuidada — um <c>Where</c> a menos — a leria como valendo para
    /// qualquer um.
    /// </summary>
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void Sem_uma_das_pecas_nao_monta(bool temToken, bool temDono, bool temVault, bool temCaminho)
    {
        var link = LinkDeNota.TentarCriar(
            temToken ? TokenDeLink.Sortear() : null,
            temDono ? ApelidoDoUsuario.De("matheus") : null,
            temVault ? NomeDoVault.De("estudo") : null,
            temCaminho ? Caminho("a.md") : null,
            DateTimeOffset.UnixEpoch);

        Assert.Null(link);
    }

    /// <summary>
    /// O ENDEREÇO É RELATIVO, sem barra na frente. O Learn é servido num sub-caminho, e uma barra
    /// inicial ignora o &lt;base href&gt;: o link sairia apontando para a raiz do domínio, que é a
    /// plataforma, e não o Learn. É a mesma regra que vale para todo href do produto.
    /// </summary>
    [Fact]
    public void O_endereco_e_relativo_ao_base_href()
    {
        var token = TokenDeLink.Sortear();
        var link = LinkDeNota.TentarCriar(
            token, ApelidoDoUsuario.De("matheus"), NomeDoVault.Padrao, Caminho("a.md"), DateTimeOffset.UnixEpoch);

        Assert.Equal($"n/{token.Valor}", link!.CaminhoNaUrl);
        Assert.DoesNotContain('/', link.CaminhoNaUrl[..1]);
    }
}
