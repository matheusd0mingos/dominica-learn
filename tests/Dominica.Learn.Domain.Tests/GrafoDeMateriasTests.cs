using Dominica.Learn.Domain.Grafo;
using Dominica.Learn.Domain.Ligacoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// O TOPO DO DRILL: o mapa de quais matérias conversam. A agregação decide o que aparece na tela de
/// entrada do grafo de um vault grande — errar aqui é mostrar uma ponte que não existe ou esconder a
/// que a prova cobra.
/// </summary>
public class GrafoDeMateriasTests
{
    private static CaminhoNota C(string caminho) => CaminhoNota.De(caminho);

    private static LigacaoResolvida Liga(string de, string para) =>
        new() { Origem = C(de), Alvo = para, Destino = C(para) };

    private static GrafoDoVault Grafo(string[] caminhos, params (string De, string Para)[] ligacoes) =>
        GrafoDoVault.Montar(caminhos.Select(C), ligacoes.Select(l => Liga(l.De, l.Para)));

    [Fact]
    public void Ligacao_que_cruza_materias_vira_ponte()
    {
        var (nos, pontes) = GrafoDeMaterias.Montar(Grafo(
            ["Direito/A.md", "Português/B.md"],
            ("Direito/A.md", "Português/B.md")));

        Assert.Equal(2, nos.Count);
        var ponte = Assert.Single(pontes);
        Assert.Equal(1, ponte.Peso);
        Assert.Equal("Direito", nos[ponte.De].Materia.Nome);
        Assert.Equal("Português", nos[ponte.Para].Materia.Nome);
    }

    [Fact]
    public void Ligacao_INTERNA_nao_vira_ponte()
    {
        // "Direito conversa com Direito" é verdade vazia. O que este mapa diz é o cruzamento; o que
        // existe dentro da matéria já está no tamanho do nó.
        var (nos, pontes) = GrafoDeMaterias.Montar(Grafo(
            ["Direito/A.md", "Direito/B.md"],
            ("Direito/A.md", "Direito/B.md")));

        Assert.Empty(pontes);
        Assert.All(nos, n => Assert.Equal(0, n.Vizinhas));
    }

    [Fact]
    public void O_peso_conta_PARES_DE_NOTAS_e_nao_citacoes()
    {
        // Um par que se cita três vezes é UMA ponte de peso 1: relação entre assuntos se mede por
        // alcance, não por prolixidade — a mesma régua das etiquetas. E é a mesma conta do aviso
        // "N ligações apontam para fora", então os dois números nunca discordam na tela.
        var (_, pontes) = GrafoDeMaterias.Montar(Grafo(
            ["Direito/A.md", "Português/B.md", "Português/C.md"],
            ("Direito/A.md", "Português/B.md"),
            ("Direito/A.md", "Português/B.md"),
            ("Direito/A.md", "Português/B.md"),
            ("Direito/A.md", "Português/C.md")));

        Assert.Equal(2, Assert.Single(pontes).Peso);
    }

    [Fact]
    public void A_ponte_nao_tem_direcao_e_e_guardada_uma_vez()
    {
        // "A cita B" e "B cita A" são a mesma ponte quando a pergunta é "esses assuntos conversam?".
        var (_, pontes) = GrafoDeMaterias.Montar(Grafo(
            ["Direito/A.md", "Português/B.md", "Direito/C.md"],
            ("Direito/A.md", "Português/B.md"),
            ("Português/B.md", "Direito/C.md")));

        var ponte = Assert.Single(pontes);
        Assert.True(ponte.De < ponte.Para);
        Assert.Equal(2, ponte.Peso);
    }

    [Fact]
    public void Nota_na_raiz_vira_o_no_Sem_materia_e_ele_fica_por_ultimo()
    {
        // Esconder as notas sem pasta faria o mapa mentir sobre o vault: elas são o conteúdo que ainda
        // não foi arquivado — informação, não ausência dela. Por último na ordem porque "Sem matéria"
        // não é um assunto: é a falta de um.
        var (nos, pontes) = GrafoDeMaterias.Montar(Grafo(
            ["Solta.md", "Direito/A.md"],
            ("Solta.md", "Direito/A.md")));

        Assert.Equal(2, nos.Count);
        Assert.False(nos[^1].Materia.Existe);
        Assert.Equal("Sem matéria", nos[^1].Materia.Rotulo);
        Assert.Single(pontes);
    }

    [Fact]
    public void O_tamanho_do_no_e_quantas_notas_a_materia_tem()
    {
        var (nos, _) = GrafoDeMaterias.Montar(Grafo(
            ["Direito/A.md", "Direito/B.md", "Direito/C.md", "Português/D.md"]));

        Assert.Equal(3, nos.Single(n => n.Materia.Nome == "Direito").Notas);
        Assert.Equal(1, nos.Single(n => n.Materia.Nome == "Português").Notas);
    }

    [Fact]
    public void Vizinhas_conta_com_quantas_materias_ha_ponte()
    {
        var (nos, _) = GrafoDeMaterias.Montar(Grafo(
            ["Direito/A.md", "Português/B.md", "Contabilidade/C.md"],
            ("Direito/A.md", "Português/B.md"),
            ("Direito/A.md", "Contabilidade/C.md")));

        Assert.Equal(2, nos.Single(n => n.Materia.Nome == "Direito").Vizinhas);
        Assert.Equal(1, nos.Single(n => n.Materia.Nome == "Português").Vizinhas);
    }

    [Fact]
    public void A_ordem_e_deterministica()
    {
        // O layout parte desta ordem. Um mapa que troca de desenho sozinho destrói a memória espacial —
        // a mesma exigência de todos os outros mapas.
        var a = GrafoDeMaterias.Montar(Grafo(["Z/A.md", "B/C.md", "M/D.md"]));
        var b = GrafoDeMaterias.Montar(Grafo(["M/D.md", "Z/A.md", "B/C.md"]));

        Assert.Equal(a.Nos.Select(n => n.Materia.Nome), b.Nos.Select(n => n.Materia.Nome));
    }

    [Fact]
    public void Vault_vazio_da_mapa_vazio()
    {
        var (nos, pontes) = GrafoDeMaterias.Montar(GrafoDoVault.Vazio);
        Assert.Empty(nos);
        Assert.Empty(pontes);
    }
}
