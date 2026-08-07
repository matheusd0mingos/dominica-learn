using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Ligacoes;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// LIGAR PELO BOTÃO É EDITAR O SEU MARKDOWN, e é por isso que isto tem mais teste que código. A nota é
/// o produto: uma inserção que estraga o texto não é um recurso ruim, é perda.
/// </summary>
public class SecaoDeRelacionadasTests
{
    private static (string, SecaoDeRelacionadas.Resultado) Ligar(string conteudo, string alvo) =>
        SecaoDeRelacionadas.Ligar(conteudo, alvo,
            AnalisadorDeNota.Analisar(conteudo, "Nota").Ligacoes.Select(l => l.Alvo));

    [Fact]
    public void Nota_sem_secao_ganha_a_secao_no_fim()
    {
        var (r, res) = Ligar("# Teste\n\nUm parágrafo.\n", "Direito tributário");

        Assert.Equal("# Teste\n\nUm parágrafo.\n\n## Relacionadas\n- [[Direito tributário]]\n", r);
        Assert.Equal(SecaoDeRelacionadas.Resultado.Ligou, res);
    }

    [Fact]
    public void O_corpo_nao_e_tocado()
    {
        // A promessa que sustenta o recurso inteiro: só se ACRESCENTA. Nada do que já estava é
        // reescrito — nem espaçamento, nem frontmatter, nem a ordem de nada.
        var original = "---\ntags: [materia]\nfavorito: true\n---\n# Mapa\n\n\nTexto   com   espaços.\n";
        var (r, _) = Ligar(original, "Teste");

        Assert.StartsWith(original.TrimEnd('\n'), r.Replace("\r\n", "\n"));
        Assert.Contains("- [[Teste]]", r);
    }

    [Fact]
    public void A_segunda_ligacao_entra_no_FIM_da_secao()
    {
        // A ordem de chegada é informação: as primeiras ligações são as que você fez ao criar a nota.
        var (r, _) = Ligar("# X\n\n## Relacionadas\n- [[Primeira]]\n", "Segunda");

        Assert.Equal("# X\n\n## Relacionadas\n- [[Primeira]]\n- [[Segunda]]\n", r);
    }

    [Fact]
    public void Ligar_duas_vezes_nao_duplica()
    {
        var (uma, _) = Ligar("# X\n", "Teste");
        var (duas, res) = Ligar(uma, "Teste");

        Assert.Equal(uma, duas);
        Assert.Equal(SecaoDeRelacionadas.Resultado.JaHavia, res);
    }

    [Fact]
    public void Ligacao_que_ja_existe_NA_PROSA_conta()
    {
        // A regra que faz o botão e o "[[" inline conviverem em vez de brigarem. Quem escreveu
        // "segue [[Lei 14.133]]" no meio do texto já ligou — repetir no fim só polui a nota e não
        // acrescenta aresta nenhuma ao grafo.
        var (r, res) = Ligar("# X\n\nO prazo aqui segue [[Lei 14.133]].\n", "Lei 14.133");

        Assert.DoesNotContain("## Relacionadas", r);
        Assert.Equal(SecaoDeRelacionadas.Resultado.JaHavia, res);
    }

    [Fact]
    public void Diferenca_de_caixa_nao_engana()
    {
        var (_, res) = Ligar("# X\n\n[[teste]]\n", "Teste");
        Assert.Equal(SecaoDeRelacionadas.Resultado.JaHavia, res);
    }

    [Fact]
    public void A_secao_no_meio_da_nota_e_respeitada_e_o_resto_fica_no_lugar()
    {
        // O DEFEITO QUE ISTO PEGA: escrever a linha depois do fim da seção, ou seja, DENTRO da seção
        // seguinte. Um cabeçalho encerra a lista.
        var original = "# X\n\n## Relacionadas\n- [[A]]\n\n## Assuntos\n- estudar isto\n";
        var (r, _) = Ligar(original, "B");

        Assert.Equal("# X\n\n## Relacionadas\n- [[A]]\n- [[B]]\n\n## Assuntos\n- estudar isto\n", r);
    }

    [Fact]
    public void Secao_dentro_de_bloco_de_codigo_nao_e_a_secao()
    {
        // Um "## Relacionadas" de exemplo dentro de ``` é texto, não estrutura. Escrever ali estragaria
        // o bloco — e num vault de estudo bloco de código é onde mora o que foi copiado de uma prova.
        var original = "# X\n\n```md\n## Relacionadas\n- [[Exemplo]]\n```\n";
        var (r, _) = Ligar(original, "Teste");

        Assert.Contains("```md\n## Relacionadas\n- [[Exemplo]]\n```", r);
        Assert.EndsWith("## Relacionadas\n- [[Teste]]\n", r);
    }

    [Fact]
    public void Nota_vazia_nao_ganha_linha_em_branco_no_topo()
    {
        var (r, _) = Ligar("", "Teste");
        Assert.Equal("## Relacionadas\n- [[Teste]]\n", r);
    }
}
