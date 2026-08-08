using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dominica.Learn.Application.Tests;

/// <summary>
/// O MAIOR CASO DE USO DO PRODUTO, e o que ficou mais tempo sem um único teste: ligações, grafo,
/// etiquetas, matérias, favoritos e templates passam todos por aqui.
///
/// O QUE ESTES TESTES PROVAM é a ORQUESTRAÇÃO, e não a regra: as regras já têm teste no domínio
/// (SecaoDeRelacionadas, SugestaoDeEtiquetas, EtiquetasDaNota, GrafoDoVault). O que só aqui dá para
/// afirmar é que este serviço chama as regras certas, na ordem certa, e — o mais importante — que ele
/// GRAVA PASSANDO PELO ServicoDeNotas. Gravar por fora faria a mudança existir no arquivo e não no
/// índice, e o índice é o que alimenta a busca, o grafo e o painel.
/// </summary>
public class ServicoDeConhecimentoTests
{
    private sealed record Cenario(
        ServicoDeConhecimento Conhecimento,
        ServicoDeNotas Notas,
        VaultEmMemoria Vault,
        IndiceEmMemoria Indice,
        HistoricoEmMemoria Historico,
        AnexosEmMemoria Anexos,
        RelogioFixo Relogio);

    private static Cenario Montar()
    {
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero));
        var vault = new VaultEmMemoria(relogio);
        var indice = new IndiceEmMemoria();
        var historico = new HistoricoEmMemoria();
        var anexos = new AnexosEmMemoria();
        var reconciliacao = new ReconciliarVault(vault, indice, relogio, NullLogger<ReconciliarVault>.Instance);
        var notas = new ServicoDeNotas(vault, indice, historico, reconciliacao, relogio, NullLogger<ServicoDeNotas>.Instance);
        var conhecimento = new ServicoDeConhecimento(
            vault, indice, new RenderizadorDeMentira(), anexos, notas, relogio,
            NullLogger<ServicoDeConhecimento>.Instance);

        return new Cenario(conhecimento, notas, vault, indice, historico, anexos, relogio);
    }

    private static async Task<CaminhoNota> Criar(Cenario c, string caminho, string conteudo)
    {
        var alvo = CaminhoNota.De(caminho);
        var r = await c.Notas.CriarAsync(alvo, conteudo);
        Assert.True(r.Ok, r.Mensagem);
        return alvo;
    }

    // —— LIGAR DUAS NOTAS ——————————————————————————————————————————————————————————————

    [Fact]
    public async Task Ligar_escreve_o_wikilink_na_nota_de_dentro()
    {
        var c = Montar();
        var a = await Criar(c, "Direito/A.md", "# A\n");
        var b = await Criar(c, "Direito/B.md", "# B\n");

        var r = await c.Conhecimento.LigarAsync(a, b);

        Assert.True(r.Ok);
        Assert.False(r.Valor!.JaHavia);
        Assert.Contains("## Relacionadas", c.Vault.Arquivos["Direito/A.md"]);
        Assert.Contains("- [[B]]", c.Vault.Arquivos["Direito/A.md"]);
        // A OUTRA NOTA NÃO É TOCADA: ligar é escrever DENTRO de uma, e a direção é a informação.
        Assert.Equal("# B\n", c.Vault.Arquivos["Direito/B.md"]);
    }

    [Fact]
    public async Task Ligar_passa_pelo_servico_de_notas_e_o_grafo_enxerga()
    {
        // A GARANTIA QUE SÓ ESTE NÍVEL DÁ. Gravar direto no repositório escreveria o arquivo e deixaria
        // o índice para trás — a ligação existiria no disco e não no grafo, que é a única tela que este
        // recurso existe para alimentar.
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n");
        var b = await Criar(c, "B.md", "# B\n");

        await c.Conhecimento.LigarAsync(a, b);

        var backlinks = await c.Indice.BacklinksAsync(b);
        Assert.Equal(a, Assert.Single(backlinks).Origem);
    }

    [Fact]
    public async Task Ligar_a_mesma_nota_duas_vezes_nao_duplica_e_diz_que_ja_havia()
    {
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n");
        var b = await Criar(c, "B.md", "# B\n");

        await c.Conhecimento.LigarAsync(a, b);
        var segunda = await c.Conhecimento.LigarAsync(a, b);

        Assert.True(segunda.Ok);
        Assert.True(segunda.Valor!.JaHavia);
        var texto = c.Vault.Arquivos["A.md"];
        Assert.Equal(1, texto.Split("- [[B]]").Length - 1);
    }

    [Fact]
    public async Task Ligar_uma_nota_a_ela_mesma_e_recusado()
    {
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n");

        var r = await c.Conhecimento.LigarAsync(a, a);

        Assert.False(r.Ok);
        Assert.Equal("# A\n", c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task Ligar_para_nota_inexistente_diz_qual_nao_achou()
    {
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n");

        var r = await c.Conhecimento.LigarAsync(a, CaminhoNota.De("Fantasma.md"));

        // O ALVO pode não existir — citar o que ainda não se escreveu é legítimo e vira "por escrever".
        // Quem não pode faltar é a nota de DENTRO, que é onde se escreve.
        var deFora = await c.Conhecimento.LigarAsync(CaminhoNota.De("Fantasma.md"), a);
        Assert.False(deFora.Ok);
        Assert.Contains("Fantasma", deFora.Mensagem);
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task O_texto_da_ligacao_e_o_mais_curto_que_ainda_resolve()
    {
        // Com homônimas, o nome sozinho levaria para a nota errada — e o erro é silencioso. Com nome
        // único, escrever o caminho inteiro deixaria a nota feia de ler no Obsidian, que é onde ela é
        // lida. Ver EscritaDeLigacao.MaisCurta.
        var c = Montar();
        var origem = await Criar(c, "Origem.md", "# Origem\n");
        await Criar(c, "Português/Crase.md", "# Crase\n");
        await Criar(c, "Direito/Crase.md", "# Crase\n");
        await Criar(c, "Sozinha.md", "# Sozinha\n");

        await c.Conhecimento.LigarAsync(origem, CaminhoNota.De("Português/Crase.md"));
        await c.Conhecimento.LigarAsync(origem, CaminhoNota.De("Sozinha.md"));

        var texto = c.Vault.Arquivos["Origem.md"];
        Assert.Contains("- [[Português/Crase]]", texto);   // homônima: precisa do caminho
        Assert.Contains("- [[Sozinha]]", texto);           // única: o nome basta
    }

    // —— ETIQUETAS DA NOTA ——————————————————————————————————————————————————————————————

    [Fact]
    public async Task Marcar_etiqueta_escreve_no_frontmatter_e_o_indice_ve()
    {
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n\nO corpo.\n");

        var r = await c.Conhecimento.MarcarEtiquetaAsync(a, "direito/penal");

        Assert.True(r.Ok);
        Assert.StartsWith("---\ntags: [direito/penal]\n---", c.Vault.Arquivos["A.md"]);
        Assert.Contains("O corpo.", c.Vault.Arquivos["A.md"]);

        // E chegou no índice: sem isso a etiqueta não apareceria na busca nem no painel.
        var achados = await c.Indice.BuscarAsync(new ConsultaDeBusca { Etiqueta = Etiqueta.TentarCriar("direito")! });
        Assert.Equal("A.md", Assert.Single(achados).Nota.Caminho.Valor);
    }

    [Fact]
    public async Task Etiqueta_invalida_e_recusada_com_o_motivo()
    {
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n");

        var r = await c.Conhecimento.MarcarEtiquetaAsync(a, "14133");

        Assert.False(r.Ok);
        Assert.Contains("14133", r.Mensagem);
        Assert.Equal("# A\n", c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task Desmarcar_tira_do_frontmatter()
    {
        var c = Montar();
        var a = await Criar(c, "A.md", "---\ntags: [direito, penal]\n---\n\n# A\n");

        var r = await c.Conhecimento.DesmarcarEtiquetaAsync(a, Etiqueta.TentarCriar("penal")!);

        Assert.True(r.Ok);
        Assert.Contains("tags: [direito]", c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task Desmarcar_etiqueta_ESCRITA_NO_TEXTO_e_recusado_e_o_arquivo_nao_muda()
    {
        // A garantia mais importante do recurso: apagar "#pegadinha" do meio de uma frase reescreveria
        // o texto da pessoa. A recusa tem de dizer POR QUE, ou o botão parece quebrado.
        var c = Montar();
        var original = "# A\n\nIsso é #pegadinha clássica.\n";
        var a = await Criar(c, "A.md", original);

        var r = await c.Conhecimento.DesmarcarEtiquetaAsync(a, Etiqueta.TentarCriar("pegadinha")!);

        Assert.False(r.Ok);
        Assert.Contains("texto", r.Mensagem);
        Assert.Equal(original, c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task Sugerir_etiquetas_ouve_o_vault_inteiro()
    {
        var c = Montar();
        await Criar(c, "1.md", "#tributario #decorar");
        await Criar(c, "2.md", "#tributario #decorar");
        await Criar(c, "3.md", "#portugues #crase");

        var s = await c.Conhecimento.SugerirEtiquetasAsync([Etiqueta.TentarCriar("tributario")!]);

        Assert.Equal("decorar", s[0].Etiqueta.Valor);
        Assert.True(s[0].PorCoocorrencia);
        Assert.Equal(2, s[0].Peso);
    }

    // —— MATÉRIAS ————————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Criar_materia_cria_a_nota_indice_dela()
    {
        var c = Montar();

        var r = await c.Conhecimento.CriarMateriaAsync("Direito Tributário");

        Assert.True(r.Ok, r.Mensagem);
        Assert.Contains("Direito Tributário/Direito Tributário.md", c.Vault.Arquivos.Keys);
        Assert.Contains("Direito Tributário/Direito Tributário.md", c.Indice.Notas.Keys);
    }

    [Fact]
    public async Task Criar_materia_que_ja_existe_e_recusado()
    {
        var c = Montar();
        await c.Conhecimento.CriarMateriaAsync("Direito");

        var r = await c.Conhecimento.CriarMateriaAsync("Direito");

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task As_materias_contam_as_notas_de_cada_pasta()
    {
        var c = Montar();
        await Criar(c, "Direito/A.md", "# A");
        await Criar(c, "Direito/B.md", "# B");
        await Criar(c, "Português/C.md", "# C");
        await Criar(c, "Solta.md", "# Solta");

        var materias = await c.Conhecimento.MateriasAsync();

        Assert.Equal(2, materias.Single(m => m.Materia.Nome == "Direito").Notas);
        Assert.Equal(1, materias.Single(m => m.Materia.Nome == "Português").Notas);
    }

    // —— FAVORITOS ————————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Favoritar_grava_no_ARQUIVO_e_nao_num_banco()
    {
        // A regra que rege o projeto: nada que exista só no índice pode ser conhecimento do usuário.
        // Favorito tem de sobreviver a um "reindexar do zero" e aparecer no Obsidian Desktop.
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n");

        var ligou = await c.Conhecimento.AlternarFavoritoAsync(a);

        Assert.True(ligou.Valor);
        Assert.Contains("favorito: true", c.Vault.Arquivos["A.md"]);

        var desligou = await c.Conhecimento.AlternarFavoritoAsync(a);

        Assert.False(desligou.Valor);
        // DESFAVORITAR REMOVE O CAMPO, em vez de gravar "false": ninguém quer carregar a lembrança
        // disso no topo do arquivo para sempre.
        Assert.DoesNotContain("favorito", c.Vault.Arquivos["A.md"]);
    }

    [Fact]
    public async Task As_favoritas_saem_do_texto_das_notas()
    {
        var c = Montar();
        var a = await Criar(c, "A.md", "# A\n");
        await Criar(c, "B.md", "# B\n");
        await c.Conhecimento.AlternarFavoritoAsync(a);

        var favoritas = await c.Conhecimento.FavoritasAsync();

        Assert.Equal("A.md", Assert.Single(favoritas).Caminho.Valor);
    }

    // —— O GRAFO ————————————————————————————————————————————————————————————————————

    [Fact]
    public async Task O_grafo_desenha_o_que_as_notas_citam()
    {
        // A ORDEM É DE PROPÓSITO: "A" cita "B" ANTES de B existir, que é como se escreve de verdade —
        // primeiro a ideia, depois a nota. Ver ReconciliarVault.ConsertarLigacoesQuebradasAsync.
        var c = Montar();
        await Criar(c, "A.md", "# A\n\nvai para [[B]]\n");
        await Criar(c, "B.md", "# B\n");
        await Criar(c, "Orfa.md", "# Órfã\n");

        var visao = await c.Conhecimento.GrafoAsync(null, 2, 800, 600, null);

        Assert.Equal(3, visao.Posicionado.Grafo.Nos.Count);
        Assert.Single(visao.Posicionado.Grafo.Arestas);
        Assert.Equal("Orfa.md", Assert.Single(visao.Posicionado.Grafo.Orfas).Caminho.Valor);
    }

    [Fact]
    public async Task O_grafo_de_uma_materia_avisa_quantas_ligacoes_saem_dela()
    {
        // Recortar ESCONDE as pontes com outras matérias, e esconder sem avisar faria o recorte parecer
        // o vault inteiro. Numa base de estudo, a ponte é o achado mais valioso.
        var c = Montar();
        await Criar(c, "Direito/A.md", "# A\n\nvai para [[Crase]]\n");
        await Criar(c, "Português/Crase.md", "# Crase\n");

        var visao = await c.Conhecimento.GrafoAsync(null, 2, 800, 600, Materia.De("Direito"));

        Assert.Equal(1, visao.LigacoesParaFora);
    }

    [Fact]
    public async Task O_mapa_de_materias_agrega_as_pontes_do_vault()
    {
        // O topo do drill: um nó por matéria, ponte = pares de notas que cruzam. A regra mora no
        // domínio (GrafoDeMaterias); aqui se prova a costura — inclusive que o par que se cita duas
        // vezes continua sendo UMA ponte de peso 1, a mesma conta do aviso "apontam para fora".
        var c = Montar();
        await Criar(c, "Português/Crase.md", "# Crase\n");
        await Criar(c, "Direito/A.md", "# A\n\nver [[Crase]] e de novo [[Crase]]\n");
        await Criar(c, "Direito/B.md", "# B\n\ninterna: [[A]]\n");

        var mapa = await c.Conhecimento.MapaDeMateriasAsync();

        Assert.Equal(2, mapa.Nos.Count);
        Assert.Equal(2, mapa.Nos.Single(n => n.Materia.Nome == "Direito").Notas);
        Assert.Equal(1, Assert.Single(mapa.Pontes).Peso);
        Assert.Equal(mapa.Nos.Count, mapa.Posicoes.Count);
    }

    [Fact]
    public async Task O_mapa_de_etiquetas_sai_do_vault_inteiro()
    {
        var c = Montar();
        await Criar(c, "1.md", "#a #b");
        await Criar(c, "2.md", "#a #b");
        await Criar(c, "3.md", "#c");

        var mapa = await c.Conhecimento.MapaDeEtiquetasAsync();

        Assert.Equal(3, mapa.Nos.Count);
        Assert.Equal(2, Assert.Single(mapa.Pares).Peso);
        Assert.Equal(mapa.Nos.Count, mapa.Posicoes.Count);
    }

    // —— TEMPLATES ————————————————————————————————————————————————————————————————————

    [Fact]
    public async Task Criar_de_template_aplica_o_roteiro_e_nao_toca_no_template()
    {
        var c = Montar();
        var template = $"{ServicoDeConhecimento.PastaDeTemplates}/Resumo.md";
        await Criar(c, template, "# {{titulo}}\n\n## Pontos\n");

        var r = await c.Conhecimento.CriarDeTemplateAsync(
            CaminhoNota.De(template), CaminhoNota.De("Direito/Licitações.md"));

        Assert.True(r.Ok, r.Mensagem);
        Assert.Contains("# Licitações", c.Vault.Arquivos["Direito/Licitações.md"]);
        Assert.Contains("## Pontos", c.Vault.Arquivos["Direito/Licitações.md"]);
        Assert.Contains("{{titulo}}", c.Vault.Arquivos[template]);   // o molde continua molde
    }

    [Fact]
    public async Task Os_templates_saem_da_pasta_de_templates_do_vault()
    {
        // Template é conhecimento do usuário — ele escreveu aquele roteiro —, então mora no vault e não
        // em configuração da aplicação.
        var c = Montar();
        await Criar(c, $"{ServicoDeConhecimento.PastaDeTemplates}/Resumo.md", "# {{titulo}}");
        await Criar(c, "Direito/Nada a ver.md", "# X");

        var templates = await c.Conhecimento.TemplatesAsync();

        Assert.Equal("Resumo", Assert.Single(templates).Nome);
    }

    // —— MATÉRIA DE REFERÊNCIA ————————————————————————————————————————————————————————

    [Fact]
    public async Task Marcar_referencia_escreve_na_nota_indice_da_materia()
    {
        var c = Montar();
        await Criar(c, "Trabalho/Trabalho.md", "# Trabalho\n");

        var r = await c.Conhecimento.AlternarReferenciaAsync(Materia.De("Trabalho"));

        Assert.True(r.Ok);
        Assert.True(r.Valor);
        Assert.Contains("referencia: true", c.Vault.Arquivos["Trabalho/Trabalho.md"]);

        var referencias = await c.Conhecimento.ReferenciasAsync();
        Assert.Contains(Materia.De("Trabalho"), referencias);
    }

    [Fact]
    public async Task Marcar_referencia_CRIA_a_nota_indice_quando_ela_nao_existe()
    {
        // Uma matéria criada direto no disco — por quem arrastou uma pasta para o vault — não tem
        // nota-índice, e a marca precisa de um arquivo onde morar.
        var c = Montar();
        await Criar(c, "Trabalho/Reunião.md", "# Reunião\n");

        var r = await c.Conhecimento.AlternarReferenciaAsync(Materia.De("Trabalho"));

        Assert.True(r.Ok, r.Mensagem);
        Assert.Contains("Trabalho/Trabalho.md", c.Vault.Arquivos.Keys);
    }

    [Fact]
    public async Task Nota_sem_materia_nao_pode_ser_referencia()
    {
        var c = Montar();

        var r = await c.Conhecimento.AlternarReferenciaAsync(Materia.Nenhuma);

        Assert.False(r.Ok);
    }

    // —— MENÇÕES NÃO LIGADAS ————————————————————————————————————————————————————————

    [Fact]
    public async Task As_mencoes_nao_ligadas_acham_quem_cita_sem_ligar()
    {
        var c = Montar();
        await Criar(c, "Prescrição.md", "# Prescrição\n");
        await Criar(c, "Tributário.md", "# Tributário\n\nA prescrição tributária corre desde o lançamento.\n");
        await Criar(c, "JaLigada.md", "# Já ligada\n\nver [[Prescrição]]\n");

        var mencoes = await c.Conhecimento.MencoesNaoLigadasAsync(CaminhoNota.De("Prescrição.md"));

        // A que JÁ liga não aparece: ela não é uma conexão por fazer.
        Assert.Equal("Tributário.md", Assert.Single(mencoes).Onde.Valor);
        Assert.Contains("prescrição", Assert.Single(mencoes).Mencao.Trecho, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ligar_uma_mencao_escreve_o_wikilink_no_lugar_da_palavra()
    {
        var c = Montar();
        await Criar(c, "Prescrição.md", "# Prescrição\n");
        var onde = await Criar(c, "Tributário.md", "# Tributário\n\nA prescrição corre desde o lançamento.\n");

        // A MENÇÃO VEM DA VARREDURA, e não é remontada aqui: ligar por uma posição inventada escreveria
        // no lugar errado do texto de alguém. É a mesma menção que a tela mostrou.
        var achada = Assert.Single(await c.Conhecimento.MencoesNaoLigadasAsync(CaminhoNota.De("Prescrição.md")));

        var r = await c.Conhecimento.LigarMencaoAsync(onde, achada.Mencao, CaminhoNota.De("Prescrição.md"));

        Assert.True(r.Ok, r.Mensagem);
        Assert.Contains("[[Prescrição|prescrição]]", c.Vault.Arquivos["Tributário.md"]);
    }

    // —— RENDERIZAR: A TRANSCLUSÃO ————————————————————————————————————————————————————
    //
    // O renderizador é síncrono e não lê nota; quem lê é a aplicação, ANTES, e entrega por função. O que
    // se prova aqui é essa entrega — o desenho da moldura já tem teste no adaptador Markdig.

    private static (Cenario C, RenderizadorDeMentira R) MontarComRenderizador()
    {
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero));
        var vault = new VaultEmMemoria(relogio);
        var indice = new IndiceEmMemoria();
        var reconciliacao = new ReconciliarVault(vault, indice, relogio, NullLogger<ReconciliarVault>.Instance);
        var notas = new ServicoDeNotas(vault, indice, new HistoricoEmMemoria(), reconciliacao, relogio,
            NullLogger<ServicoDeNotas>.Instance);
        var renderizador = new RenderizadorDeMentira();
        var conhecimento = new ServicoDeConhecimento(
            vault, indice, renderizador, new AnexosEmMemoria(), notas, relogio,
            NullLogger<ServicoDeConhecimento>.Instance);
        return (new Cenario(conhecimento, notas, vault, indice, new HistoricoEmMemoria(),
            new AnexosEmMemoria(), relogio), renderizador);
    }

    [Fact]
    public async Task Renderizar_entrega_o_conteudo_da_nota_embutida()
    {
        var (c, renderizador) = MontarComRenderizador();
        await Criar(c, "Direito/Outra.md", "# Outra\n\ncorpo da outra\n");

        await c.Conhecimento.RenderizarAsync("veja ![[Outra]] aqui");

        Assert.Contains("corpo da outra", renderizador.Transcluidos["Outra"]);
    }

    [Fact]
    public async Task Embed_de_nota_inexistente_nao_entrega_conteudo()
    {
        var (c, renderizador) = MontarComRenderizador();

        await c.Conhecimento.RenderizarAsync("veja ![[Não Existe]]");

        Assert.Empty(renderizador.Transcluidos);
    }

    [Fact]
    public async Task Wikilink_comum_nao_carrega_conteudo_nenhum()
    {
        // Só o EMBED custa leitura. Se o link comum também carregasse, abrir uma nota-índice com
        // cinquenta [[links]] viraria cinquenta leituras de disco por render.
        var (c, renderizador) = MontarComRenderizador();
        await Criar(c, "Direito/Outra.md", "# Outra\n");

        await c.Conhecimento.RenderizarAsync("veja [[Outra]] aqui");

        Assert.Empty(renderizador.Transcluidos);
    }

    // —— AS NOTAS COM AS DUAS ETIQUETAS — a linha do mapa ————————————————————————————

    [Fact]
    public async Task Notas_com_ambas_e_INTERSECAO()
    {
        var c = Montar();
        await Criar(c, "A.md", "# A\n\n#pegadinha #errei-na-prova\n");
        await Criar(c, "B.md", "# B\n\n#pegadinha\n");
        await Criar(c, "C.md", "# C\n\n#errei-na-prova\n");

        var notas = await c.Conhecimento.NotasComAmbasAsync(
            Etiqueta.TentarCriar("pegadinha")!, Etiqueta.TentarCriar("errei-na-prova")!);

        Assert.Equal("A.md", Assert.Single(notas).Caminho.Valor);
    }

    [Fact]
    public async Task Notas_com_ambas_desce_a_hierarquia_dos_dois_lados()
    {
        // A linha do mapa liga as etiquetas LITERAIS, mas a consulta desce a árvore como toda consulta
        // de etiqueta do produto — a lista pode ser maior que o peso da linha, nunca menor.
        var c = Montar();
        await Criar(c, "A.md", "# A\n\n#direito/penal #prova/cespe\n");
        await Criar(c, "B.md", "# B\n\n#direito/penal\n");

        var notas = await c.Conhecimento.NotasComAmbasAsync(
            Etiqueta.TentarCriar("direito")!, Etiqueta.TentarCriar("prova")!);

        Assert.Equal("A.md", Assert.Single(notas).Caminho.Valor);
    }

    [Fact]
    public async Task Notas_com_ambas_sem_par_nenhum_da_lista_vazia()
    {
        var c = Montar();
        await Criar(c, "A.md", "# A\n\n#pegadinha\n");

        Assert.Empty(await c.Conhecimento.NotasComAmbasAsync(
            Etiqueta.TentarCriar("pegadinha")!, Etiqueta.TentarCriar("decorar")!));
    }

    // —— RENOMEAR/MESCLAR ETIQUETA ————————————————————————————————————————————————————

    [Fact]
    public async Task Renomear_etiqueta_reescreve_todas_as_notas_e_o_indice_enxerga()
    {
        var c = Montar();
        await Criar(c, "A.md", "# A\n\nsobre #pegadinh\n");
        await Criar(c, "B.md", "---\ntags: [pegadinh]\n---\n\n# B\n");
        await Criar(c, "C.md", "# C\n\nsem nada\n");

        var r = await c.Conhecimento.RenomearEtiquetaAsync(Etiqueta.TentarCriar("pegadinh")!, "pegadinha");

        Assert.True(r.Ok, r.Mensagem);
        Assert.Equal(2, r.Valor);
        Assert.Contains("#pegadinha", c.Vault.Arquivos["A.md"]);
        Assert.Contains("pegadinha", c.Vault.Arquivos["B.md"]);
        Assert.Equal("# C\n\nsem nada\n", c.Vault.Arquivos["C.md"]);

        // A GARANTIA DESTE NÍVEL: passou pelo ServicoDeNotas, então o índice já sabe — a etiqueta velha
        // sumiu da busca e a nova responde.
        var comNova = await c.Indice.BuscarAsync(new Application.Portas.ConsultaDeBusca
        { Etiqueta = Etiqueta.TentarCriar("pegadinha"), Limite = 10 });
        Assert.Equal(2, comNova.Count);
        var comVelha = await c.Indice.BuscarAsync(new Application.Portas.ConsultaDeBusca
        { Etiqueta = Etiqueta.TentarCriar("pegadinh"), Limite = 10 });
        Assert.Empty(comVelha);
    }

    [Fact]
    public async Task Renomear_para_nome_invalido_e_recusado_sem_tocar_em_nada()
    {
        var c = Montar();
        await Criar(c, "A.md", "# A\n\n#pegadinh\n");

        var r = await c.Conhecimento.RenomearEtiquetaAsync(Etiqueta.TentarCriar("pegadinh")!, "###");

        Assert.False(r.Ok);
        Assert.Contains("#pegadinh", c.Vault.Arquivos["A.md"]);
    }
}
