using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Tests;

/// <summary>
/// Esta é a única classe do sistema cujo defeito entrega o conhecimento de uma pessoa a outra sem dar
/// erro nenhum. Cada teste aqui é uma forma de burlá-la — e é por isso que ela foi escrita pura: o teste
/// que custa três linhas é o teste que ainda vai existir daqui a cinco anos.
/// </summary>
public class PermissaoNoVaultTests
{
    private static ApelidoDoUsuario Quem(string apelido) =>
        ApelidoDoUsuario.TentarCriar(apelido, out var a, out var erro) && a is not null
            ? a
            : throw new InvalidOperationException($"apelido de teste inválido: {erro}");

    private static readonly ApelidoDoUsuario Ana = Quem("ana");
    private static readonly ApelidoDoUsuario Bruno = Quem("bruno");
    private static readonly ApelidoDoUsuario Carla = Quem("carla");

    private const string Materia = "Contabilidade Avançada";

    private static CaminhoNota C(string caminho) => CaminhoNota.De(caminho);

    private static Concessao Concede(
        ApelidoDoUsuario dono, string pasta, ApelidoDoUsuario convidado,
        PapelNoCompartilhamento papel = PapelNoCompartilhamento.Leitor) =>
        Concessao.TentarCriar(dono, pasta, convidado, papel)
        ?? throw new InvalidOperationException("concessão de teste inválida");

    // —— O DONO ————————————————————————————————————————————————————————————————————————

    [Fact]
    public void ODonoEscreveNoProprioVaultInteiro()
    {
        Assert.Equal(Acesso.Escrita, PermissaoNoVault.Para(Bruno, Bruno, C("Qualquer/Coisa.md"), []));
    }

    [Fact]
    public void ODonoNaoPrecisaDeConcessaoNemDeListaNenhuma()
    {
        Assert.Equal(Acesso.Escrita, PermissaoNoVault.Para(Bruno, Bruno, C("x.md"), null));
    }

    // —— SEM CONCESSÃO ——————————————————————————————————————————————————————————————————

    [Fact]
    public void SemConcessaoNinguemAlcancaOVaultDeOutro()
    {
        Assert.Equal(Acesso.Negado, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/CPC 06.md"), []));
    }

    [Fact]
    public void ListaNulaEhNegado()
    {
        Assert.Equal(Acesso.Negado, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/x.md"), null));
    }

    [Fact]
    public void FaltandoSaberQuemPedeEhNegado()
    {
        // "Não sei quem é você" nunca pode virar "pode entrar". Isto acontece de verdade quando um serviço
        // de fundo chama um caminho que só faz sentido para gente logada.
        var concessao = Concede(Bruno, Materia, Ana, PapelNoCompartilhamento.Editor);

        Assert.Equal(Acesso.Negado, PermissaoNoVault.Para(null, Bruno, C($"{Materia}/x.md"), [concessao]));
        Assert.Equal(Acesso.Negado, PermissaoNoVault.Para(Ana, null, C($"{Materia}/x.md"), [concessao]));
        Assert.Equal(Acesso.Negado, PermissaoNoVault.Para(Ana, Bruno, null, [concessao]));
    }

    // —— OS PAPÉIS ——————————————————————————————————————————————————————————————————————

    [Fact]
    public void LeitorLeENaoEscreve()
    {
        var concessao = Concede(Bruno, Materia, Ana);

        Assert.Equal(Acesso.Leitura, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/CPC 06.md"), [concessao]));
        Assert.False(PermissaoNoVault.PodeEscrever(Ana, Bruno, C($"{Materia}/CPC 06.md"), [concessao]));
    }

    [Fact]
    public void EditorEscreve()
    {
        var concessao = Concede(Bruno, Materia, Ana, PapelNoCompartilhamento.Editor);

        Assert.Equal(Acesso.Escrita, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/CPC 06.md"), [concessao]));
    }

    // —— OS LIMITES DA PASTA ————————————————————————————————————————————————————————————

    [Fact]
    public void PastaVizinhaComNomeParecidoNaoEhAlcancada()
    {
        // O ERRO CLÁSSICO, e o que mais importa deste arquivo: "Contabilidade Avançada 2" COMEÇA com
        // "Contabilidade Avançada" e não está dentro dela. Comparando prefixo de texto, a concessão
        // vazaria para uma pasta que ninguém compartilhou — e sem dar erro nenhum.
        var concessao = Concede(Bruno, Materia, Ana, PapelNoCompartilhamento.Editor);

        Assert.Equal(Acesso.Negado,
            PermissaoNoVault.Para(Ana, Bruno, C($"{Materia} 2/Segredo.md"), [concessao]));
    }

    [Fact]
    public void PastaQueEhPrefixoDaConcedidaTambemNao()
    {
        var concessao = Concede(Bruno, "Direito Administrativo", Ana);

        Assert.Equal(Acesso.Negado, PermissaoNoVault.Para(Ana, Bruno, C("Direito/Licitações.md"), [concessao]));
    }

    [Fact]
    public void NotaNaRaizDoVaultNuncaEhAlcancada()
    {
        // Nota solta na raiz não está dentro de pasta nenhuma. Se fosse alcançada, compartilhar uma
        // matéria exporia o que estivesse solto no vault.
        var concessao = Concede(Bruno, Materia, Ana, PapelNoCompartilhamento.Editor);

        Assert.Equal(Acesso.Negado, PermissaoNoVault.Para(Ana, Bruno, C("Diário.md"), [concessao]));
    }

    [Fact]
    public void SubpastaDaConcedidaEhAlcancada()
    {
        // A matéria pode ter subpastas — "Contabilidade Avançada/Anexos". Excluí-las quebraria a nota que
        // referencia a imagem.
        var concessao = Concede(Bruno, Materia, Ana);

        Assert.Equal(Acesso.Leitura,
            PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/Anexos/quadro.png.md"), [concessao]));
    }

    [Fact]
    public void CaixaDiferenteNaoAlcanca()
    {
        // O caminho da nota é sensível a maiúsculas neste produto — é o nome do arquivo no disco, e no
        // Linux "contabilidade" e "Contabilidade" são duas pastas. Casar sem olhar caixa daria acesso a
        // uma pasta diferente da que foi concedida.
        var concessao = Concede(Bruno, Materia, Ana);

        Assert.Equal(Acesso.Negado,
            PermissaoNoVault.Para(Ana, Bruno, C("contabilidade avançada/CPC 06.md"), [concessao]));
    }

    // —— CONCESSÃO DE OUTRA PESSOA ——————————————————————————————————————————————————————

    [Fact]
    public void ConcessaoDadaAOutroConvidadoNaoServe()
    {
        var paraCarla = Concede(Bruno, Materia, Carla, PapelNoCompartilhamento.Editor);

        Assert.Equal(Acesso.Negado, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/x.md"), [paraCarla]));
    }

    [Fact]
    public void ConcessaoSobreOutroVaultNaoServe()
    {
        // Ana é editora da matéria de Carla. Isso não lhe dá nada no vault de Bruno, mesmo que a pasta
        // tenha o mesmo nome — e pastas de matéria têm o mesmo nome o tempo todo.
        var noVaultDaCarla = Concede(Carla, Materia, Ana, PapelNoCompartilhamento.Editor);

        Assert.Equal(Acesso.Negado, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/x.md"), [noVaultDaCarla]));
    }

    [Fact]
    public void ConcessoesDeTerceirosNaListaNaoAtrapalham()
    {
        var ruido = Concede(Carla, "Português", Bruno, PapelNoCompartilhamento.Editor);
        var minha = Concede(Bruno, Materia, Ana);

        Assert.Equal(Acesso.Leitura, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/x.md"), [ruido, minha]));
    }

    // —— DUAS CONCESSÕES PARA A MESMA PASTA ————————————————————————————————————————————

    [Fact]
    public void AMaisPermissivaVence()
    {
        // Só aparece por engano de quem concedeu, e no engano a intenção mais recente foi promover. Negar
        // seria a escolha "segura" e produziria o suporte impossível de explicar: "eu te dei acesso de
        // editor e continua sem funcionar".
        var leitor = Concede(Bruno, Materia, Ana);
        var editor = Concede(Bruno, Materia, Ana, PapelNoCompartilhamento.Editor);

        Assert.Equal(Acesso.Escrita, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/x.md"), [leitor, editor]));
        Assert.Equal(Acesso.Escrita, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/x.md"), [editor, leitor]));
    }

    // —— A CONCESSÃO MALFORMADA NEM NASCE ——————————————————————————————————————————————

    [Fact]
    public void ConcessaoAninhadaEhRecusada()
    {
        // "Direito/Licitações" criaria concessões sobrepostas — duas regras valendo para o mesmo arquivo,
        // uma dizendo "lê" e outra "escreve". Sobreposição é onde nasce o vazamento indepurável.
        Assert.Null(Concessao.TentarCriar(Bruno, "Direito/Licitações", Ana, PapelNoCompartilhamento.Leitor));
    }

    [Fact]
    public void ConcessaoParaSiMesmoEhRecusada()
    {
        Assert.Null(Concessao.TentarCriar(Bruno, Materia, Bruno, PapelNoCompartilhamento.Editor));
    }

    [Fact]
    public void ConcessaoSemPastaEhRecusada()
    {
        // Pasta vazia significaria "o vault inteiro" — que é justamente o que a decisão de desenho
        // excluiu. Deixar passar transformaria um campo em branco em compartilhar tudo.
        Assert.Null(Concessao.TentarCriar(Bruno, "", Ana, PapelNoCompartilhamento.Leitor));
        Assert.Null(Concessao.TentarCriar(Bruno, "   ", Ana, PapelNoCompartilhamento.Leitor));
        Assert.Null(Concessao.TentarCriar(Bruno, "/", Ana, PapelNoCompartilhamento.Leitor));
    }

    [Fact]
    public void ConcessaoComSubidaDeDiretorioEhRecusada()
    {
        Assert.Null(Concessao.TentarCriar(Bruno, "..", Ana, PapelNoCompartilhamento.Editor));
        Assert.Null(Concessao.TentarCriar(Bruno, ".", Ana, PapelNoCompartilhamento.Editor));
        Assert.Null(Concessao.TentarCriar(Bruno, "../outro", Ana, PapelNoCompartilhamento.Editor));
    }

    [Fact]
    public void BarrasEmVoltaSaoAparadas()
    {
        // "/Contabilidade Avançada/" digitado à mão é a mesma pasta. Recusar seria pedantismo; guardar com
        // as barras faria a comparação por segmento nunca casar.
        var concessao = Concessao.TentarCriar(Bruno, $"/{Materia}/", Ana, PapelNoCompartilhamento.Leitor);

        Assert.NotNull(concessao);
        Assert.Equal(Materia, concessao.Pasta);
        Assert.Equal(Acesso.Leitura, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/x.md"), [concessao]));
    }

    // —— A COMPARAÇÃO É POR VALOR ——————————————————————————————————————————————————————

    [Fact]
    public void ApelidosIguaisVindosDeInstanciasDiferentesCasam()
    {
        // ESTE TESTE EXISTE POR CAUSA DOS OUTROS. Todos eles usam as mesmas instâncias de apelido, então
        // passariam intactos se a comparação fosse por REFERÊNCIA — e aí, em produção, o apelido vindo do
        // banco nunca casaria com o da sessão e ninguém teria acesso a nada. Ou pior: uma implementação
        // futura poderia inverter isso.
        var anaDoBanco = Quem("ana");
        var brunoDoBanco = Quem("bruno");
        var concessao = Concede(brunoDoBanco, Materia, anaDoBanco, PapelNoCompartilhamento.Editor);

        Assert.NotSame(Ana, anaDoBanco);
        Assert.Equal(Acesso.Escrita, PermissaoNoVault.Para(Ana, Bruno, C($"{Materia}/x.md"), [concessao]));
    }
}
