using Dominica.Learn.Domain.Vault;
using Dominica.Learn.Infrastructure.Vault;

namespace Dominica.Learn.Infrastructure.Tests;

/// <summary>
/// A MIGRAÇÃO É A ÚNICA PARTE DOS VAULTS QUE MEXE NOS ARQUIVOS DE ALGUÉM, e por isso ela tem mais teste
/// que código. Coluna de banco errada se refaz; nota perdida não volta de lugar nenhum.
///
/// O teste central é <see cref="Nada_se_perde_nem_se_altera"/>: ele não confere um caminho, confere que
/// o CONJUNTO de arquivos e o conteúdo de cada um são idênticos antes e depois. É a única forma de
/// afirmar "nada se perdeu" sem depender de eu ter lembrado de todos os casos.
/// </summary>
public class MigracaoParaVaultsTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "learn-migracao-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ApelidoDoUsuario _matheus = ApelidoDoUsuario.De("matheus");

    public void Dispose()
    {
        if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void Escrever(string relativo, string conteudo)
    {
        var caminho = Path.Combine(_raiz, relativo.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
    }

    /// <summary>Todos os arquivos abaixo de uma pasta, como (caminho relativo → conteúdo).</summary>
    private static Dictionary<string, string> Arquivos(string pasta) =>
        !Directory.Exists(pasta)
            ? []
            : Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    f => Path.GetRelativePath(pasta, f).Replace(Path.DirectorySeparatorChar, '/'),
                    File.ReadAllText);

    private ResultadoDaMigracao Migrar() =>
        MigracaoParaVaults.Migrar(_raiz, _matheus, NomeDoVault.Padrao);

    // —— A GARANTIA CENTRAL ——————————————————————————————————————————————————————————

    [Fact]
    public void Nada_se_perde_nem_se_altera()
    {
        Escrever("matheus/Direito/Licitações.md", "# Licitações\n\nLei 14.133.");
        Escrever("matheus/Direito/Administrativo/Atos.md", "# Atos");
        Escrever("matheus/Português/Crase.md", "# Crase");
        Escrever("matheus/Solta na raiz.md", "sem matéria");
        Escrever("matheus/anexos/prova.pdf", "%PDF-falso");
        Escrever("matheus/.obsidian/workspace.json", "{}");

        var antes = Arquivos(Path.Combine(_raiz, "matheus"));
        Assert.Equal(6, antes.Count);

        var r = Migrar();

        var depois = Arquivos(Path.Combine(_raiz, "matheus", "estudo"));

        // MESMO CONJUNTO, MESMO CONTEÚDO, MESMA QUANTIDADE. Comparar o dicionário inteiro é o que torna
        // este teste uma afirmação sobre "nada se perdeu", e não sobre os casos de que eu me lembrei.
        Assert.Equal(antes, depois);

        // E nada ficou para trás na pasta do usuário além do próprio vault.
        Assert.Equal("estudo", Assert.Single(
            Directory.EnumerateFileSystemEntries(Path.Combine(_raiz, "matheus")).Select(Path.GetFileName)));
        Assert.True(r.Houve);
    }

    [Fact]
    public void O_que_estava_escondido_vai_junto()
    {
        // ".obsidian" é a configuração do Obsidian Desktop, e ".git" existe em quem versiona o vault.
        // Deixá-los para trás faria a pessoa perder plugins, workspace e HISTÓRICO — sem mensagem
        // nenhuma, porque nada do que o app mostra depende deles.
        Escrever("matheus/.obsidian/plugins/x/main.js", "// plugin");
        Escrever("matheus/.git/HEAD", "ref: refs/heads/main");
        Escrever("matheus/Direito/A.md", "#");

        Migrar();

        Assert.True(File.Exists(Path.Combine(_raiz, "matheus", "estudo", ".obsidian", "plugins", "x", "main.js")));
        Assert.True(File.Exists(Path.Combine(_raiz, "matheus", "estudo", ".git", "HEAD")));
    }

    // —— AS GUARDAS ——————————————————————————————————————————————————————————————————

    [Fact]
    public void Rodar_duas_vezes_nao_aninha_vault_dentro_de_vault()
    {
        // Esta rotina roda em TODO arranque. Se a segunda passada descesse "estudo" para dentro de
        // "estudo", o segundo deploy quebraria o vault de todo mundo — e o primeiro pareceria certo.
        Escrever("matheus/Direito/A.md", "#");
        Migrar();

        var r = Migrar();

        Assert.False(r.Houve);
        Assert.True(File.Exists(Path.Combine(_raiz, "matheus", "estudo", "Direito", "A.md")));
        Assert.False(Directory.Exists(Path.Combine(_raiz, "matheus", "estudo", "estudo")));
    }

    [Fact]
    public void Ambiguidade_para_a_migracao_em_vez_de_ser_resolvida_no_chute()
    {
        // Existe "estudo" E existe coisa solta ao lado. Isso tanto pode ser uma migração interrompida
        // quanto uma MATÉRIA chamada "estudo" — e as duas leituras pedem coisas opostas. Não mexer deixa
        // o vault exatamente como estava, que é o único estado sempre reversível.
        Escrever("matheus/estudo/Aula.md", "pode ser matéria, pode ser vault");
        Escrever("matheus/Direito/A.md", "solto");

        var r = Migrar();

        Assert.False(r.Houve);
        Assert.Equal("pode ser matéria, pode ser vault",
            File.ReadAllText(Path.Combine(_raiz, "matheus", "estudo", "Aula.md")));
        Assert.Equal("solto", File.ReadAllText(Path.Combine(_raiz, "matheus", "Direito", "A.md")));
    }

    [Fact]
    public void Pasta_vazia_de_usuario_novo_passa_direto()
    {
        Directory.CreateDirectory(Path.Combine(_raiz, "matheus"));

        var r = Migrar();

        Assert.False(r.Houve);
        // Não cria "estudo" à toa: quem nunca escreveu nada não tem vault para migrar, e a pasta nasce
        // sozinha na primeira gravação.
        Assert.False(Directory.Exists(Path.Combine(_raiz, "matheus", "estudo")));
    }

    [Fact]
    public void Usuario_que_nao_existe_no_disco_nao_e_inventado()
    {
        var r = Migrar();

        Assert.False(r.Houve);
        Assert.False(Directory.Exists(Path.Combine(_raiz, "matheus")));
    }

    [Fact]
    public void Materia_com_nome_valido_de_vault_desce_junto()
    {
        // O DEFEITO QUE ESTA CLASSE JÁ TEVE, e o motivo de ela não adivinhar mais nada pelo nome.
        //
        // A primeira versão perguntava "esta subpasta tem nome válido de vault?" para decidir o que
        // deixar onde estava. "Direito" tem. "trabalho" tem. "estudo" tem. Todas essas matérias teriam
        // ficado na pasta do usuário — visíveis no disco, invisíveis no app, e sem mensagem nenhuma.
        Escrever("matheus/Direito/A.md", "matéria de nome curto");
        Escrever("matheus/trabalho/B.md", "matéria que parece vault");
        Escrever("matheus/Direito tributário/C.md", "matéria com espaço e acento");

        Migrar();

        Assert.Equal("matéria de nome curto", File.ReadAllText(Path.Combine(_raiz, "matheus", "estudo", "Direito", "A.md")));
        Assert.Equal("matéria que parece vault", File.ReadAllText(Path.Combine(_raiz, "matheus", "estudo", "trabalho", "B.md")));
        Assert.Equal("matéria com espaço e acento", File.ReadAllText(Path.Combine(_raiz, "matheus", "estudo", "Direito tributário", "C.md")));
    }

    // —— A VARREDURA DE TODOS ————————————————————————————————————————————————————————

    [Fact]
    public void Migra_todo_mundo_e_ignora_o_que_nao_e_pasta_de_usuario()
    {
        Escrever("matheus/Direito/A.md", "a");
        Escrever("joao/Física/B.md", "b");
        Escrever(".git/config", "sobra de cópia");        // não é apelido válido: ignorado

        var feitos = MigracaoParaVaults.MigrarTodos(_raiz, NomeDoVault.Padrao);

        Assert.Equal(2, feitos.Count);
        Assert.True(File.Exists(Path.Combine(_raiz, "matheus", "estudo", "Direito", "A.md")));
        Assert.True(File.Exists(Path.Combine(_raiz, "joao", "estudo", "Física", "B.md")));
        Assert.False(Directory.Exists(Path.Combine(_raiz, ".git", "estudo")));
    }
}
