using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Reconciliacao;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

public class MateriasDoVaultTests
{
    /// <summary>
    /// Índice que só sabe listar caminhos. É tudo que <see cref="ServicoDeConhecimento.MateriasAsync"/>
    /// consulta — e essa é justamente a propriedade que vale registrar: a divisão por matéria não custa
    /// leitura de disco nem coluna nova, porque o caminho já carrega a resposta.
    /// </summary>
    private sealed class IndiceSoDeCaminhos(params string[] caminhos) : IIndiceDoVault
    {
        public Task<IReadOnlyList<CaminhoNota>> TodosOsCaminhosAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CaminhoNota>>(caminhos.Select(CaminhoNota.De).ToList());

        public Task<IReadOnlyList<EstadoDaNota>> EstadoAtualAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task IndexarAsync(Nota n, IReadOnlyList<LigacaoResolvida> l, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoverAsync(CaminhoNota c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RenomearAsync(CaminhoNota de, CaminhoNota para, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NotaIndexada?> ObterAsync(CaminhoNota c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<NotaConhecida>> NotasConhecidasAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Acerto>> BuscarAsync(ConsultaDeBusca q, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LigacaoResolvida>> BacklinksAsync(CaminhoNota c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LigacaoResolvida>> LigacoesDeAsync(CaminhoNota c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EtiquetaContada>> EtiquetasAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<NotaIndexada>> RecentesAsync(int limite, CancellationToken ct = default) => throw new NotSupportedException();
    }

    // Os demais colaboradores são nulos de propósito: se algum dia MateriasAsync passar a usar disco ou
    // renderizador, este teste quebra alto — que é exatamente o aviso desejado.
    private static ServicoDeConhecimento Montar(params string[] caminhos) =>
        new(null!, new IndiceSoDeCaminhos(caminhos), null!, null!, null!, null!,
            NullLogger<ServicoDeConhecimento>.Instance);

    [Fact]
    public async Task Agrupa_as_notas_pela_primeira_pasta()
    {
        var materias = await Montar(
            "Direito/Licitações.md", "Direito/Administrativo/Atos.md", "Português/Crase.md").MateriasAsync();

        Assert.Equal(["Direito", "Português"], materias.Select(m => m.Materia.Nome));
        Assert.Equal(2, materias[0].Notas);   // a subpasta continua contando para Direito
        Assert.Equal(1, materias[1].Notas);
    }

    [Fact]
    public async Task Notas_soltas_ficam_por_ultimo_e_nao_no_topo()
    {
        // "Sem matéria" é a caixa de entrada do vault. No topo, ela empurraria as matérias de verdade
        // para baixo justamente quando há muita coisa por arquivar — que é quando a lista mais importa.
        var materias = await Montar("Zoologia/Aves.md", "Solta.md", "Outra solta.md").MateriasAsync();

        Assert.Equal("Zoologia", materias[0].Materia.Nome);
        Assert.False(materias[^1].Materia.Existe);
        Assert.Equal(2, materias[^1].Notas);
    }

    [Fact]
    public async Task A_pasta_de_templates_nao_e_uma_materia()
    {
        // Template é ferramenta, não conteúdo de estudo. Na lista, apareceria competindo com Direito.
        var materias = await Montar(
            $"{ServicoDeConhecimento.PastaDeTemplates}/Resumo de aula.md", "Direito/A.md").MateriasAsync();

        Assert.Equal("Direito", Assert.Single(materias).Materia.Nome);
    }

    [Fact]
    public async Task Ordem_alfabetica_respeita_acento()
    {
        // "Órfãs" e "Ortografia": ordenação ordinal jogaria o Ó depois do Z, e a lista de matérias de um
        // vault em português ficaria com buracos onde o olho não espera.
        var materias = await Montar("Ortografia/A.md", "Órfãs/B.md", "Penal/C.md").MateriasAsync();

        Assert.Equal(["Órfãs", "Ortografia", "Penal"], materias.Select(m => m.Materia.Nome));
    }

    [Fact]
    public async Task Vault_vazio_devolve_lista_vazia()
    {
        Assert.Empty(await Montar().MateriasAsync());
    }

    // —— CRIAR MATÉRIA ————————————————————————————————————————————————————————————————
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Direito/Administrativo")]
    [InlineData("Direito\\Administrativo")]
    public async Task Nome_invalido_de_materia_e_recusado(string nome)
    {
        // Com barra seria uma SUBmatéria, e submatéria é a pasta de dentro — ela nasce junto com a
        // primeira nota que mora lá, não por aqui.
        var r = await Montar().CriarMateriaAsync(nome);
        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.Invalida, r.Motivo);
    }

    [Fact]
    public void A_nota_mapa_nao_nasce_com_ligacao_quebrada()
    {
        // Apareceu na tela: o texto de ajuda cita "[[wikilinks]]" para explicar como ligar assuntos, e
        // o analisador o leu como ligação de verdade — toda matéria nova nascia com um item falso em
        // "ainda por escrever". Entre crases, é código, e o analisador ignora.
        var conteudo = ServicoDeConhecimento.ConteudoDaNotaIndice(Materia.De("Português"));
        var analise = AnalisadorDeNota.Analisar(conteudo, "Português.md");

        Assert.Empty(analise.Ligacoes);
        Assert.Contains("`[[wikilinks]]`", conteudo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Materia_que_ja_tem_nota_nao_e_recriada()
    {
        // Já existe se QUALQUER nota mora nela, e não só se a nota-mapa existe: criar por cima de uma
        // matéria com conteúdo daria a impressão de ter começado do zero.
        var r = await Montar("Direito/Licitações.md").CriarMateriaAsync("Direito");
        Assert.False(r.Ok);
        Assert.Equal(MotivoDaFalha.JaExiste, r.Motivo);
    }

    [Fact]
    public async Task Rotulo_da_materia_ausente_e_o_mesmo_da_lista_e_do_grafo()
    {
        // As duas telas precisam chamar a mesma coisa pelo mesmo nome. Foi assim que apareceu o defeito
        // irmão deste: a lista escondia a pasta Templates e a legenda do grafo a mostrava como matéria.
        var materias = await Montar("Solta.md").MateriasAsync();
        Assert.Equal("Sem matéria", Assert.Single(materias).Materia.Rotulo);
    }
}
