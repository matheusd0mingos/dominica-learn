using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// A captura é o gesto de MENOR paciência do produto inteiro — uma ideia no meio de outra coisa. Um erro
/// aqui perde a ideia ou suja a nota do dia, e a pessoa para de confiar no botão.
/// </summary>
public class NotaDiariaTests
{
    private static readonly DateOnly Sexta = new(2026, 8, 7);

    [Fact]
    public void O_caminho_e_a_pasta_Diario_com_o_dia_em_ISO()
    {
        Assert.Equal("Diário/2026-08-07.md", NotaDiaria.CaminhoDe(Sexta).Valor);
    }

    [Fact]
    public void O_titulo_diz_o_dia_por_extenso_em_portugues()
    {
        Assert.Equal("# sexta-feira, 07/08/2026\n", NotaDiaria.ConteudoInicial(Sexta));
    }

    [Fact]
    public void A_primeira_captura_entra_com_hora_apos_o_titulo()
    {
        var novo = NotaDiaria.Capturar(NotaDiaria.ConteudoInicial(Sexta), new TimeOnly(14, 32), "Ideia solta");

        Assert.Equal("# sexta-feira, 07/08/2026\n\n- **14:32** — Ideia solta\n", novo);
    }

    [Fact]
    public void Capturas_seguidas_formam_uma_lista_continua_sem_linha_em_branco()
    {
        var um = NotaDiaria.Capturar(NotaDiaria.ConteudoInicial(Sexta), new TimeOnly(9, 0), "Primeira");
        var dois = NotaDiaria.Capturar(um, new TimeOnly(9, 5), "Segunda");

        Assert.Contains("- **09:00** — Primeira\n- **09:05** — Segunda\n", dois);
    }

    [Fact]
    public void Captura_multilinha_vira_continuacao_indentada_do_MESMO_item()
    {
        var novo = NotaDiaria.Capturar("# dia\n", new TimeOnly(10, 0), "Título da ideia\ncom um detalhe");

        Assert.Contains("- **10:00** — Título da ideia\n  com um detalhe\n", novo);
    }

    [Fact]
    public void Captura_vazia_nao_muda_nada()
    {
        var conteudo = NotaDiaria.ConteudoInicial(Sexta);
        Assert.Equal(conteudo, NotaDiaria.Capturar(conteudo, new TimeOnly(1, 0), "   "));
    }
}

/// <summary>
/// O índice é o MOC: uma nota comum que centraliza um assunto. O risco de borda é a etiqueta
/// hierárquica — "/" no nome do arquivo criaria uma subpasta sem querer.
/// </summary>
public class NotaIndiceTests
{
    [Fact]
    public void O_caminho_fica_na_raiz_com_o_nome_da_etiqueta()
    {
        Assert.Equal("Índice — prazo.md", NotaIndice.CaminhoDe(Etiqueta.TentarCriar("prazo")!).Valor);
    }

    [Fact]
    public void Etiqueta_hierarquica_NAO_vira_subpasta()
    {
        Assert.Equal("Índice — direito·penal.md", NotaIndice.CaminhoDe(Etiqueta.TentarCriar("direito/penal")!).Valor);
    }

    [Fact]
    public void O_conteudo_tem_titulo_a_propria_etiqueta_e_um_link_por_nota()
    {
        var texto = NotaIndice.Conteudo(Etiqueta.TentarCriar("prazo")!, ["Prescrição", "Direito/Decadência"]);

        Assert.Contains("# Índice — #prazo", texto);
        // A etiqueta no corpo é o que pendura o índice no mapa de etiquetas — ele é PARTE do assunto.
        Assert.Contains("\n#prazo\n", texto);
        Assert.Contains("- [[Prescrição]]", texto);
        Assert.Contains("- [[Direito/Decadência]]", texto);
    }
}
