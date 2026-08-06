using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

public class MateriaTests
{
    [Fact]
    public void Materia_e_a_primeira_pasta()
    {
        Assert.Equal("Direito", Materia.De(CaminhoNota.De("Direito/Licitações.md")).Nome);
        // Continua sendo a PRIMEIRA mesmo com subpastas: "Direito/Administrativo/Atos" é tudo Direito.
        Assert.Equal("Direito", Materia.De(CaminhoNota.De("Direito/Administrativo/Atos.md")).Nome);
    }

    [Fact]
    public void Nota_na_raiz_nao_tem_materia()
    {
        var m = Materia.De(CaminhoNota.De("Cronograma.md"));
        Assert.False(m.Existe);
        Assert.Equal(Materia.Nenhuma, m);
        // Aparece na tela como "Sem matéria" — é a caixa de entrada do vault, não um vazio escondido.
        Assert.Equal("Sem matéria", m.Rotulo);
    }

    [Fact]
    public void Mover_a_nota_muda_a_materia_sem_reindexar_nada()
    {
        // É a razão de a matéria sair do caminho: não existe estado a sincronizar. Arrastar a pasta no
        // Obsidian já é trocar de matéria, e a reconciliação seguinte não precisa saber disso.
        var solta = CaminhoNota.De("Licitações.md");
        Assert.False(Materia.De(solta).Existe);
        Assert.Equal("Direito", Materia.De(solta.MoverPara("Direito")).Nome);
    }

    [Fact]
    public void Acentuacao_e_espaco_no_nome_da_materia_sobrevivem()
    {
        Assert.Equal("Raciocínio Lógico", Materia.De(CaminhoNota.De("Raciocínio Lógico/Proposições.md")).Nome);
    }

    [Fact]
    public void Caixa_diferente_e_materia_diferente()
    {
        // Deliberado, e diferente da Etiqueta: no Linux "Direito" e "direito" são DUAS pastas. Juntá-las
        // aqui faria a tela mostrar uma matéria só enquanto o filtro por pasta — prefixo de caminho —
        // acharia as notas de apenas uma. Melhor deixar o erro de digitação visível.
        Assert.NotEqual(Materia.De("Direito"), Materia.De("direito"));
    }

    [Fact]
    public void Materias_iguais_sao_a_mesma_chave_de_dicionario()
    {
        var mapa = new Dictionary<Materia, int> { [Materia.De("Direito")] = 1 };
        Assert.True(mapa.ContainsKey(Materia.De(CaminhoNota.De("Direito/Qualquer.md"))));
    }
}
