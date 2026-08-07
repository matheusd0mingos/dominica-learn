using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Infrastructure.Vault;

/// <summary>O que a migração fez na pasta de uma pessoa. Existe para poder ser conferido e testado.</summary>
public sealed record ResultadoDaMigracao(ApelidoDoUsuario Apelido, int ItensMovidos, string Destino)
{
    public static ResultadoDaMigracao NadaAFazer(ApelidoDoUsuario a) => new(a, 0, string.Empty);
    public bool Houve => Destino.Length > 0;
}

/// <summary>
/// A MUDANÇA DE UM NÍVEL NO DISCO: o que estava em <c>{raiz}/matheus</c> passa para
/// <c>{raiz}/matheus/estudo</c>, e a partir daí cada pasta ali dentro é um vault.
///
/// ESTA É A ÚNICA PARTE DESTE RECURSO QUE MEXE NOS ARQUIVOS DE ALGUÉM, e é por isso que ela é uma classe
/// própria, pura o bastante para ser testada com um vault de mentira, em vez de dez linhas soltas dentro
/// do arranque. O resto do recurso é coluna de banco e tela — se sair errado, refaz. Aqui, não: nota
/// perdida não volta de lugar nenhum, e o vault é o produto.
///
/// AS QUATRO GUARDAS, e cada uma existe por um jeito diferente de isto dar errado:
///
///   1. NÃO ADIVINHA PELO NOME o que é vault e o que é matéria. A primeira versão desta classe
///      perguntava "esta subpasta tem nome válido de vault?" para decidir o que deixar onde estava — e
///      "Direito" TEM nome válido de vault. A matéria inteira teria ficado para trás: visível no disco,
///      invisível no app. O teste pegou; se não tivesse, teria pegado o vault de alguém.
///
///      A regra que sobrou não adivinha nada: ANTES DA MIGRAÇÃO NÃO EXISTE VAULT NENHUM, por definição.
///      Tudo que está na pasta do usuário é conteúdo, e tudo desce.
///
///   2. SÓ MIGRA UMA VEZ, e o que registra isso é a própria existência da pasta de destino. Nada de
///      arquivo de marcação: o estado do disco é o estado, e um marcador seria mais uma coisa para
///      dessincronizar.
///
///   3. AMBIGUIDADE NÃO É RESOLVIDA NO CHUTE — ela PARA a migração daquele usuário, alto, no log. O caso
///      é este: existe uma pasta "estudo" E existe outra coisa solta ao lado. Isso tanto pode ser uma
///      migração pela metade quanto uma MATÉRIA chamada "estudo". Migrar por cima misturaria duas
///      árvores; não migrar deixa o vault exatamente como estava, que é sempre reversível.
///
///   4. É TUDO OU NADA POR ITEM, e o que falha PARA a migração daquele usuário com a exceção subindo.
///      Uma migração que segue depois de falhar no meio deixa metade do vault num lugar e metade no
///      outro, que é o pior estado possível: parece funcionar, e falta coisa.
///
///   5. MOVE, NÃO COPIA. Cópia dobraria o disco no meio do arranque e deixaria a origem para trás, e aí
///      alguém em algum momento apagaria "a duplicada" — sem saber qual das duas o app estava usando.
///
/// COMO SE DESFAZ, se um dia for preciso: mover o conteúdo de <c>{raiz}/{apelido}/estudo</c> de volta
/// para <c>{raiz}/{apelido}</c> e apagar a pasta vazia. É um comando, e é assim de propósito.
/// </summary>
public static class MigracaoParaVaults
{
    /// <summary>
    /// Migra a pasta de um usuário, se precisar. Devolve o que foi feito — inclusive "nada".
    /// </summary>
    public static ResultadoDaMigracao Migrar(
        string raizComum, ApelidoDoUsuario apelido, NomeDoVault destino, ILogger? log = null)
    {
        var pastaDoUsuario = Path.Combine(Path.GetFullPath(raizComum), apelido.Valor);
        if (!Directory.Exists(pastaDoUsuario)) return ResultadoDaMigracao.NadaAFazer(apelido);

        var pastaDoVault = Path.Combine(pastaDoUsuario, destino.Valor);

        // TUDO que está na pasta do usuário, menos a pasta de destino. Sem filtro por nome — ver a
        // guarda 1 no resumo: antes da migração não existe vault nenhum, então tudo aqui é conteúdo.
        var soltos = Directory.EnumerateFileSystemEntries(pastaDoUsuario)
            .Where(c => !string.Equals(Path.GetFileName(c), destino.Valor, StringComparison.Ordinal))
            .ToList();

        // Guarda 2: nada solto = já migrado (ou usuário novo). É o caminho de todo arranque depois do
        // primeiro, e é o que torna esta rotina segura de chamar sempre.
        if (soltos.Count == 0) return ResultadoDaMigracao.NadaAFazer(apelido);

        // Guarda 3: destino existe E ainda há coisa solta ao lado. Não dá para saber se isto é uma
        // migração interrompida ou uma MATÉRIA chamada "estudo" — e as duas leituras pedem coisas
        // opostas. Não mexer deixa o vault como estava, que é o único estado sempre reversível.
        if (Directory.Exists(pastaDoVault))
        {
            log?.LogError(
                "NÃO migrei o vault de {Apelido}: já existe uma pasta \"{Destino}\" e ainda há {Quantos} " +
                "item(ns) ao lado dela. Pode ser uma migração interrompida, ou uma matéria com esse nome — " +
                "e as duas coisas pedem tratamentos opostos. Resolva à mão em {Pasta}.",
                apelido, destino, soltos.Count, pastaDoUsuario);
            return ResultadoDaMigracao.NadaAFazer(apelido);
        }

        log?.LogWarning(
            "Migrando o vault de {Apelido}: {Quantos} item(ns) descem para a pasta \"{Destino}\". " +
            "A partir de agora cada pasta em {Pasta} é um vault.",
            apelido, soltos.Count, destino, pastaDoUsuario);

        Directory.CreateDirectory(pastaDoVault);

        var movidos = 0;
        foreach (var origem in soltos)
        {
            var alvo = Path.Combine(pastaDoVault, Path.GetFileName(origem));

            // Guarda 4: qualquer falha SOBE. Ver o resumo — meia migração é pior que nenhuma.
            if (Directory.Exists(origem)) Directory.Move(origem, alvo);
            else File.Move(origem, alvo);

            movidos++;
        }

        log?.LogWarning("Vault de {Apelido} migrado: {Quantos} item(ns) em {Destino}.", apelido, movidos, pastaDoVault);
        return new ResultadoDaMigracao(apelido, movidos, pastaDoVault);
    }

    /// <summary>
    /// Roda a migração para TODAS as pastas de usuário da raiz. É o que o arranque chama.
    ///
    /// A FALHA DE UM NÃO PARA OS OUTROS — mesma regra do vigia. Mas ela é registrada como erro, e não
    /// engolida: um vault que não migrou vai aparecer vazio para o dono, e alguém precisa poder ligar
    /// uma coisa à outra sem adivinhar.
    /// </summary>
    public static IReadOnlyList<ResultadoDaMigracao> MigrarTodos(
        string raizComum, NomeDoVault destino, ILogger? log = null)
    {
        var feitos = new List<ResultadoDaMigracao>();
        var raiz = Path.GetFullPath(raizComum);
        if (!Directory.Exists(raiz)) return feitos;

        foreach (var pasta in Directory.EnumerateDirectories(raiz))
        {
            var nome = Path.GetFileName(pasta);
            if (!ApelidoDoUsuario.TentarCriar(nome, out var apelido, out _) || apelido is null) continue;

            try
            {
                var r = Migrar(raiz, apelido, destino, log);
                if (r.Houve) feitos.Add(r);
            }
            catch (Exception e)
            {
                log?.LogError(e, "Falha ao migrar o vault de {Apelido}. O vault dele ficou como estava.", apelido);
            }
        }

        return feitos;
    }
}
