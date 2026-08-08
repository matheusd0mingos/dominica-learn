using System.Globalization;
using System.Text;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Estudo;

/// <summary>Um bloco do ciclo: uma matéria por um tanto de minutos, feito ou não NESTA volta.</summary>
public sealed record BlocoDoCiclo(Materia Materia, int Minutos, bool Concluido);

/// <summary>
/// O CICLO DE ESTUDOS — o rodízio de matérias que responde "o que estudo agora?".
///
/// É o método que quase todo concurseiro usa: em vez de "hoje é dia de Direito", você tem uma FILA de
/// blocos (Direito 50min, Português 30min, Direito 50min, …) e vai girando. O peso de uma matéria é
/// quantas vezes / por quanto tempo ela aparece na fila — matéria que cai mais, ou que você erra mais,
/// entra mais. Terminou a volta inteira, recomeça. O que ele resolve é a paralisia da escolha e o viés
/// de estudar só o que se gosta.
///
/// —— POR QUE O CICLO MORA NUMA NOTA DO VAULT ————————————————————————————————————————
///
/// O plano de estudo é do usuário, e a regra do projeto é clara: o que é do usuário vive no .md, não num
/// banco que um "reindexar do zero" apaga. Guardado como nota, o ciclo sincroniza, sobrevive a tudo, e
/// ABRE NO OBSIDIAN — dá para reordenar os blocos no celular no ônibus. O formato é uma lista de tarefas
/// de Markdown comum, que o Obsidian já entende:
///
///     - [ ] Direito · 50 min
///     - [x] Português · 30 min      (o [x] é o bloco JÁ FEITO nesta volta)
///
/// A CLASSE É PURA: texto entra, texto sai. Quem lê e grava a nota é a aplicação.
/// </summary>
public static class CicloDeEstudos
{
    /// <summary>Onde o ciclo mora — uma nota na raiz do vault.</summary>
    public const string CaminhoDaNota = "Ciclo de estudos.md";

    /// <summary>Minutos de um bloco quando a nota não diz — o padrão de um "Pomodoro longo" de concurso.</summary>
    public const int MinutosPadrao = 50;

    /// <summary>
    /// Lê os blocos da nota. Linhas que não são item de tarefa (o título, texto solto) são ignoradas —
    /// a pessoa pode escrever o que quiser em volta e o ciclo só enxerga a lista.
    /// </summary>
    public static IReadOnlyList<BlocoDoCiclo> Ler(string? conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo)) return [];

        var blocos = new List<BlocoDoCiclo>();
        foreach (var linha in conteudo.Replace("\r\n", "\n").Split('\n'))
        {
            var s = linha.TrimStart();
            // "- [ ] " ou "- [x] " (ou *, +, e X maiúsculo) — o marcador de tarefa do Markdown.
            if (s.Length < 6 || s[0] is not ('-' or '*' or '+') || s[1] != ' ' || s[2] != '[' || s[4] != ']') continue;
            var concluido = s[3] is not ' ';
            var resto = s[5..].Trim();

            // "Matéria · 50 min" — o "·" separa o nome do tempo. Sem "·", a linha inteira é a matéria e
            // o tempo é o padrão: exigir a pontuação certa afastaria quem digitou o ciclo na pressa.
            var ponto = resto.IndexOf('·');
            var nome = (ponto >= 0 ? resto[..ponto] : resto).Trim();
            var minutos = ponto >= 0 ? PrimeiroInteiro(resto[(ponto + 1)..]) : MinutosPadrao;

            var materia = Materia.De(nome);
            if (materia.Existe && minutos > 0) blocos.Add(new BlocoDoCiclo(materia, minutos, concluido));
        }
        return blocos;
    }

    /// <summary>A nota do ciclo a partir dos blocos — o título fixo mais a lista de tarefas.</summary>
    public static string Escrever(IReadOnlyList<BlocoDoCiclo> blocos)
    {
        ArgumentNullException.ThrowIfNull(blocos);
        var sb = new StringBuilder();
        sb.Append("# Ciclo de estudos\n\n");
        if (blocos.Count == 0)
            sb.Append("Ainda sem blocos. Some matérias e minutos, e o ciclo diz o que estudar agora.\n");
        foreach (var b in blocos)
            sb.Append("- [").Append(b.Concluido ? 'x' : ' ').Append("] ")
              .Append(b.Materia.Nome).Append(" · ").Append(b.Minutos).Append(" min\n");
        return sb.ToString();
    }

    /// <summary>
    /// O bloco de AGORA: o primeiro ainda não feito nesta volta. Null quando não há bloco nenhum.
    ///
    /// Quando todos já foram feitos, o índice volta para o zero — a volta acabou e o ciclo recomeça
    /// sozinho, que é exatamente o que "girar o ciclo" quer dizer.
    /// </summary>
    public static int? IndiceAtual(IReadOnlyList<BlocoDoCiclo> blocos)
    {
        if (blocos.Count == 0) return null;
        var i = IndiceDoPrimeiroPendente(blocos);
        return i ?? 0;   // todos feitos → recomeça do zero (nova volta, ver Concluir)
    }

    /// <summary>
    /// Marca um bloco como feito e devolve o ciclo novo. Se com isso a volta INTEIRA fica feita, ela é
    /// zerada na hora — a próxima leitura já aponta para o começo, e não para um ciclo todo riscado.
    /// </summary>
    public static IReadOnlyList<BlocoDoCiclo> Concluir(IReadOnlyList<BlocoDoCiclo> blocos, int indice)
    {
        ArgumentNullException.ThrowIfNull(blocos);
        if (indice < 0 || indice >= blocos.Count) return blocos;

        var novos = blocos.Select((b, i) => i == indice ? b with { Concluido = true } : b).ToList();
        return novos.All(b => b.Concluido) ? Zerar(novos) : novos;
    }

    /// <summary>Recomeça a volta: todos os blocos voltam a pendentes. Usado no botão "recomeçar".</summary>
    public static IReadOnlyList<BlocoDoCiclo> Zerar(IReadOnlyList<BlocoDoCiclo> blocos)
    {
        ArgumentNullException.ThrowIfNull(blocos);
        return [.. blocos.Select(b => b with { Concluido = false })];
    }

    private static int? IndiceDoPrimeiroPendente(IReadOnlyList<BlocoDoCiclo> blocos)
    {
        for (var i = 0; i < blocos.Count; i++)
            if (!blocos[i].Concluido) return i;
        return null;
    }

    private static int PrimeiroInteiro(string texto)
    {
        var digitos = new string([.. texto.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit)]);
        return int.TryParse(digitos, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : MinutosPadrao;
    }
}
