using Dominica.Learn.Domain.Anexos;

namespace Dominica.Learn.Domain.Tests;

// O nome de arquivo vindo de um upload é a entrada mais hostil que este produto recebe: ele chega do
// navegador, cai numa chamada de sistema de arquivos e nunca passou por campo validado. Os testes de
// escape aqui não são borda — são o requisito.
public class CaminhoDeAnexoTests
{
    // —— ESCAPE DO VAULT ————————————————————————————————————————————————————————————————
    [Theory]
    [InlineData("../../etc/senha.png", "senha.png")]
    [InlineData("..\\..\\Windows\\System32\\config.png", "config.png")]
    [InlineData("/etc/shadow.png", "shadow.png")]
    public void Caminho_que_veio_no_nome_do_upload_e_descartado(string enviado, string esperado)
    {
        Assert.True(CaminhoDeAnexo.TentarDeUpload(enviado, out var c, out _));
        // Sobra só o último segmento: o que veio de pasta é DESCARTADO, não interpretado.
        Assert.Equal($"{CaminhoDeAnexo.PastaPadrao}/{esperado}", c!.Valor);
    }

    [Fact]
    public void Referencia_com_ponto_ponto_nao_vira_caminho()
    {
        // Este é o caminho que chega de um `![[../../.env]]` escrito dentro de uma nota — a rota de
        // anexos o entrega direto ao domínio.
        Assert.False(CaminhoDeAnexo.TentarCriar("Anexos/../../.env", out _, out var erro));
        Assert.Contains("\"..\"", erro);
    }

    [Fact]
    public void Caminho_absoluto_de_windows_e_recusado()
    {
        Assert.False(CaminhoDeAnexo.TentarCriar("C:/Users/alguem/foto.png", out _, out var erro));
        Assert.NotNull(erro);
    }

    // —— LISTA DE PERMISSÃO ————————————————————————————————————————————————————————————
    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.js")]
    [InlineData("planilha.xlsx")]
    [InlineData("semextensao")]
    public void Extensao_fora_da_lista_nao_entra(string nome)
    {
        Assert.False(CaminhoDeAnexo.TentarDeUpload(nome, out _, out var erro));
        Assert.Contains("não são aceitos", erro);
    }

    [Fact]
    public void Svg_e_recusado_de_proposito()
    {
        // SVG é XML que executa script. Servido da própria origem, a CSP com 'self' não ajuda — uma
        // imagem que roda código não é uma imagem.
        Assert.False(CaminhoDeAnexo.TentarDeUpload("desenho.svg", out _, out _));
        Assert.False(TiposDeAnexo.EhPermitida(".svg"));
    }

    [Theory]
    [InlineData("foto.png")]
    [InlineData("Resumo.PDF")]
    [InlineData("aula.mp3")]
    public void Extensao_da_lista_entra_na_pasta_de_anexos(string nome)
    {
        Assert.True(CaminhoDeAnexo.TentarDeUpload(nome, out var c, out _));
        Assert.StartsWith($"{CaminhoDeAnexo.PastaPadrao}/", c!.Valor, StringComparison.Ordinal);
    }

    // —— SANEAMENTO DO NOME ————————————————————————————————————————————————————————————
    [Fact]
    public void Caracteres_que_quebram_o_vault_em_outro_sistema_viram_hifen()
    {
        // O vault sincroniza para máquinas Windows. Um nome legal no Linux e ilegal lá quebraria a
        // sincronização inteira, não só aquele arquivo.
        Assert.True(CaminhoDeAnexo.TentarDeUpload("re:latório|final?.png", out var c, out _));
        Assert.Equal($"{CaminhoDeAnexo.PastaPadrao}/re-latório-final-.png", c!.Valor);
    }

    [Fact]
    public void Cerquilha_e_colchete_saem_do_nome()
    {
        // "#" e "[]" têm significado DENTRO do wikilink: "![[foto[1].png]]" fecharia o embed no lugar
        // errado e a imagem nunca apareceria. O saneamento aqui é o que mantém o embed gerável.
        Assert.True(CaminhoDeAnexo.TentarDeUpload("foto[1]#capa.png", out var c, out _));
        Assert.DoesNotContain('[', c!.Valor);
        Assert.DoesNotContain('#', c.Valor);
    }

    [Fact]
    public void Nome_que_some_na_limpeza_e_recusado()
    {
        Assert.False(CaminhoDeAnexo.TentarDeUpload("   ", out _, out var erro));
        Assert.NotNull(erro);
    }

    // —— COLISÃO ————————————————————————————————————————————————————————————————————————
    [Fact]
    public void Sufixo_entra_antes_da_extensao()
    {
        // "captura de tela.png" é o nome de arquivo mais repetido que existe. Sobrescrever em silêncio
        // apagaria o anexo de uma nota antiga.
        var c = CaminhoDeAnexo.De("Anexos/captura de tela.png");
        Assert.Equal("Anexos/captura de tela-2.png", c.ComSufixo(2).Valor);
    }

    [Fact]
    public void Sufixo_em_arquivo_sem_extensao_vai_para_o_fim()
    {
        var c = CaminhoDeAnexo.De("Anexos/arquivo");
        Assert.Equal("Anexos/arquivo-2", c.ComSufixo(2).Valor);
    }

    [Fact]
    public void Ponto_na_pasta_nao_e_confundido_com_extensao()
    {
        // "v1.2" na pasta tem ponto e não é extensão de nada. Sem a checagem de posição, o sufixo
        // entraria no meio do nome da PASTA e o arquivo iria para outro lugar.
        var c = CaminhoDeAnexo.De("Anexos/v1.2/arquivo");
        Assert.Equal("Anexos/v1.2/arquivo-2", c.ComSufixo(2).Valor);
    }

    // —— CLASSIFICAÇÃO ————————————————————————————————————————————————————————————————
    [Fact]
    public void Imagem_e_reconhecida_independente_da_caixa()
    {
        Assert.True(CaminhoDeAnexo.De("Anexos/Foto.PNG").EhImagem);
        Assert.False(CaminhoDeAnexo.De("Anexos/edital.pdf").EhImagem);
    }

    [Fact]
    public void Tipo_mime_vem_da_lista_e_nao_do_conteudo()
    {
        Assert.Equal("image/jpeg", TiposDeAnexo.TipoDe(".jpg"));
        Assert.Equal("application/pdf", TiposDeAnexo.TipoDe(".pdf"));
        // Extensão desconhecida jamais vira text/html: servir HTML da própria origem é XSS entregue.
        Assert.Equal("application/octet-stream", TiposDeAnexo.TipoDe(".desconhecida"));
    }

    [Fact]
    public void Nota_md_nao_e_anexo()
    {
        // O endpoint de anexos é autenticado, mas serviria QUALQUER arquivo do vault se ".md" estivesse
        // na lista — publicando as notas por uma rota que não passa pelo controle de acesso das páginas.
        Assert.False(TiposDeAnexo.EhPermitida(".md"));
    }
}
