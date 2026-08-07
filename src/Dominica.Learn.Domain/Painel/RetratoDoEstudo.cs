using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Painel;

/// <summary>Uma matéria vista do painel. Tudo aqui sai dos arquivos — nada é digitado por ninguém.</summary>
public sealed record MateriaNoPainel(
    Materia Materia,
    int Notas,
    int Cartoes,
    /// <summary>
    /// Cartões JÁ RESPONDIDOS que venceram. Não inclui inéditos — eles são a coluna ao lado, e somá-los
    /// aqui faria a matéria parecer atrasada quando ela é só nova.
    /// </summary>
    int Vencidos,
    int Ineditos,
    /// <summary>Cartões com intervalo longo — o que já está assentado.</summary>
    int Maduros,
    /// <summary>
    /// Média da facilidade dos cartões JÁ RESPONDIDOS, em porcentagem (250 = 2,5×). Zero quando nenhum
    /// cartão foi respondido ainda — e aí não há sinal nenhum, que é diferente de "está fácil".
    /// </summary>
    int Facilidade);

/// <summary>O que merece atenção, e por quê. O texto é a metade que importa.</summary>
public sealed record Atencao(TipoDeAtencao Tipo, string Titulo, string Motivo);

public enum TipoDeAtencao
{
    /// <summary>A pessoa escreveu e não fez cartão nenhum: nada daquilo vai voltar.</summary>
    MateriaSemCartoes,
    /// <summary>Facilidade média abaixo do padrão: a matéria está custando.</summary>
    MateriaCustando,
    /// <summary>Ligação quebrada: você citou e ainda não escreveu.</summary>
    PorEscrever,
}

/// <summary>Um bloco do "o que agora". <see cref="Motivo"/> é obrigatório — ver o comentário da classe.</summary>
public sealed record Sugestao(string Titulo, string Motivo, Materia Materia, TipoDeSugestao Tipo);

public enum TipoDeSugestao
{
    Revisar,
    FazerCartoes,
    Escrever,
}

/// <summary>Tudo que o painel mostra, numa foto só.</summary>
public sealed record RetratoDoEstudo(
    /// <summary>Revisões vencidas: cartões já respondidos cuja data chegou. Não inclui inéditos.</summary>
    int RevisoesVencidas,
    /// <summary>
    /// Inéditos que o teto libera hoje. É o número que a fila vai de fato entregar — e por isso ele,
    /// e não o total de inéditos, é o que o painel mostra.
    /// </summary>
    int NovosHoje,
    /// <summary>Inéditos que o teto segurou para os próximos dias. Existe para o painel poder explicar.</summary>
    int NovosGuardados,
    int IntroduzidosHoje,
    int Teto,
    int TotalDeNotas,
    int TotalDeCartoes,
    int Suspensos,
    IReadOnlyList<MateriaNoPainel> Materias,
    IReadOnlyList<Sugestao> Agora,
    IReadOnlyList<Atencao> Atencoes)
{
    public static readonly RetratoDoEstudo Vazio =
        new(0, 0, 0, 0, 0, 0, 0, 0, [], [], []);

    /// <summary>
    /// O que a fila vai entregar hoje, somando revisão e estreia.
    ///
    /// É ESTE o número que a tela mostra como "a revisar hoje", e não o total de vencidos. Com um teto de
    /// 5 e 36 inéditos, o total diria 36 e a fila entregaria 2 — dois números da mesma tela discordando,
    /// que é a forma mais rápida de a pessoa parar de confiar no painel. Aconteceu na primeira versão.
    /// </summary>
    public int ParaHoje => RevisoesVencidas + NovosHoje;
}

/// <summary>
/// O painel de estudo, montado a partir do que EXISTE — e nada mais.
///
/// POR QUE ESTE PAINEL NÃO TEM AS MÉTRICAS QUE UM PAINEL DE ESTUDO COSTUMA TER:
///
/// Horas estudadas, questões resolvidas, percentual de acerto, progresso do edital, curva de evolução —
/// nada disso é calculável hoje, porque não existe registro de desempenho nem edital. Mostrar esses
/// números com dados inventados, zerados ou estimados seria pior que não mostrá-los: um painel de estudo
/// existe para ser a base de uma decisão diária, e um número errado ali muda a decisão para pior.
///
/// O QUE DÁ PARA RESPONDER SÓ COM OS ARQUIVOS, e é bastante:
///
///   • o que revisar hoje, e em qual matéria — sai do agendamento gravado nos .md;
///   • onde o estudo está CUSTANDO — sai da facilidade do SM-2, que cai quando se erra. Não é o mesmo
///     que percentual de acerto em questões, e é o sinal mais próximo disso que existe sem registro;
///   • o que foi escrito e nunca virou cartão — que é conhecimento que não vai voltar;
///   • o que foi citado e nunca escrito — as ligações quebradas são a lista do que você mesmo decidiu
///     estudar e ainda não estudou.
///
/// O QUE NÃO ENTRA, mesmo sendo tentador: um mapa de calor de atividade. A marca de agendamento guarda
/// UMA data por cartão — a da última resposta —, então um cartão respondido cinco vezes só conta uma.
/// O calor de ontem seria razoável e o do mês passado seria ficção decrescente. Vem quando houver
/// registro de sessões.
///
/// A CLASSE É PURA. Recebe cartões, contagem de notas e ligações quebradas; devolve a foto. É o que
/// permite provar as regras de prioridade — que vão mudar toda semana no começo — sem subir nada.
/// </summary>
// O nome NÃO pode ser "Painel": é o do namespace, e C# resolve o namespace primeiro — "Painel.Montar"
// viraria "namespace.Montar" e não compila em lugar nenhum que use o tipo de fora.
public static class PainelDeEstudo
{
    /// <summary>Acima disto o cartão está assentado: o SM-2 já o empurrou para além de três semanas.</summary>
    public const int IntervaloDeMaduro = 21;

    /// <summary>
    /// Abaixo da facilidade inicial (250), a matéria já custou erros. Não é um limiar arbitrário: 250 é
    /// exatamente onde todo cartão começa, então qualquer valor menor é histórico de dificuldade.
    /// </summary>
    public const int FacilidadeDeAtrito = Agendamento.FacilidadePadrao;

    /// <summary>Quantas sugestões o "o que agora" mostra. Mais que isto deixa de ser prioridade.</summary>
    public const int MaximoDeSugestoes = 3;

    public static RetratoDoEstudo Montar(
        IReadOnlyList<Cartao> cartoes,
        IReadOnlyDictionary<Materia, int> notasPorMateria,
        IReadOnlyList<string> porEscrever,
        DateOnly hoje,
        int teto)
    {
        ArgumentNullException.ThrowIfNull(cartoes);
        ArgumentNullException.ThrowIfNull(notasPorMateria);
        ArgumentNullException.ThrowIfNull(porEscrever);

        var vivos = cartoes.Where(c => !c.Suspenso).ToList();
        var porMateria = vivos.GroupBy(c => Materia.De(c.Nota)).ToDictionary(g => g.Key, g => g.ToList());

        // A UNIÃO das matérias que têm nota com as que têm cartão. Uma matéria só com notas precisa
        // aparecer — ela é justamente o caso mais acionável do painel.
        var todas = notasPorMateria.Keys.Concat(porMateria.Keys).Distinct().ToList();

        var materias = todas
            .Select(m =>
            {
                var seus = porMateria.GetValueOrDefault(m) ?? [];
                var respondidos = seus.Where(c => c.Agendamento is not null).ToList();

                return new MateriaNoPainel(
                    m,
                    notasPorMateria.GetValueOrDefault(m),
                    seus.Count,
                    // Só os já respondidos: o inédito tem coluna própria.
                    seus.Count(c => c.Agendamento is { } a && a.Vencido(hoje)),
                    seus.Count(TetoDeCartoesNovos.EhInedito),
                    seus.Count(c => c.Agendamento is { } a && a.IntervaloEmDias >= IntervaloDeMaduro),
                    respondidos.Count == 0 ? 0 : (int)Math.Round(respondidos.Average(c => c.Agendamento!.Facilidade)));
            })
            // Mais vencidos primeiro: é a ordem em que a pessoa vai agir.
            .OrderByDescending(m => m.Vencidos)
            .ThenByDescending(m => m.Cartoes)
            .ThenBy(m => m.Materia.Rotulo, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var ineditos = vivos.Count(TetoDeCartoesNovos.EhInedito);
        var introduzidos = TetoDeCartoesNovos.IntroduzidosHoje(vivos, hoje);
        var novosHoje = teto <= TetoDeCartoesNovos.SemTeto
            ? ineditos
            : Math.Min(ineditos, Math.Max(0, teto - introduzidos));

        return new RetratoDoEstudo(
            RevisoesVencidas: vivos.Count(c => c.Agendamento is { } a && a.Vencido(hoje)),
            NovosHoje: novosHoje,
            NovosGuardados: ineditos - novosHoje,
            IntroduzidosHoje: introduzidos,
            Teto: teto,
            TotalDeNotas: notasPorMateria.Values.Sum(),
            TotalDeCartoes: vivos.Count,
            Suspensos: cartoes.Count(c => c.Suspenso),
            Materias: materias,
            Agora: Agora(materias, porEscrever, novosHoje, ineditos - novosHoje),
            Atencoes: Atencoes(materias, porEscrever));
    }

    /// <summary>
    /// O "o que agora": no máximo três blocos, cada um com o MOTIVO escrito.
    ///
    /// O motivo não é enfeite — é o que separa uma sugestão de uma ordem. Sem ele, a pessoa obedece sem
    /// entender e para de obedecer na primeira vez que discorda; com ele, ela pode discordar com
    /// informação, que é o que se quer de um assistente e não de um chefe.
    ///
    /// A ORDEM DE PRIORIDADE É UMA DECISÃO DE PRODUTO, não um cálculo:
    ///
    ///   1. revisão vencida vence tudo — é conhecimento JÁ CONQUISTADO prestes a ser perdido, e recuperá-lo
    ///      custa minutos contra as horas que custou aprender;
    ///   2. depois a matéria que está custando — é onde o esforço rende mais por hora;
    ///   3. depois o que foi escrito e não virou cartão — conhecimento que não vai voltar sozinho;
    ///   4. por último, escrever o que falta. É o mais valioso a longo prazo e o menos urgente hoje.
    /// </summary>
    private static IReadOnlyList<Sugestao> Agora(
        IReadOnlyList<MateriaNoPainel> materias, IReadOnlyList<string> porEscrever,
        int novosHoje, int novosGuardados)
    {
        var sugestoes = new List<Sugestao>();

        // A MATÉRIA ENTRA NA SUGESTÃO SE ELA CONTRIBUI PARA A FILA DE HOJE — e a fila é revisão vencida
        // MAIS estreia liberada pelo teto. Olhar só o vencido foi o erro da primeira versão: com uma
        // matéria só de cartões inéditos, o painel anunciava "2 para hoje" e mandava fazer outra coisa.
        var contribuem = materias
            .Where(m => m.Vencidos > 0 || (m.Ineditos > 0 && novosHoje > 0))
            .OrderByDescending(m => m.Vencidos)
            .ThenByDescending(m => m.Ineditos)
            .Take(2);

        foreach (var m in contribuem)
            sugestoes.Add(new Sugestao(
                $"Revisar {m.Materia.Rotulo}",
                Motivo(m, novosGuardados) +
                (m.Facilidade is > 0 and < FacilidadeDeAtrito ? " E é a matéria que mais tem custado." : ""),
                m.Materia, TipoDeSugestao.Revisar));

        var custando = materias
            .Where(m => m.Vencidos == 0 && m.Facilidade is > 0 and < FacilidadeDeAtrito)
            .OrderBy(m => m.Facilidade)
            .FirstOrDefault();
        if (custando is not null && sugestoes.Count < MaximoDeSugestoes)
            sugestoes.Add(new Sugestao(
                $"Voltar em {custando.Materia.Rotulo}",
                "Os cartões desta matéria estão ficando mais difíceis a cada revisão — é onde uma hora rende mais.",
                custando.Materia, TipoDeSugestao.Revisar));

        var semCartoes = materias
            .Where(m => m.Cartoes == 0 && m.Notas > 0)
            .OrderByDescending(m => m.Notas)
            .FirstOrDefault();
        if (semCartoes is not null && sugestoes.Count < MaximoDeSugestoes)
            sugestoes.Add(new Sugestao(
                $"Fazer cartões em {semCartoes.Materia.Rotulo}",
                $"{semCartoes.Notas} nota(s) escrita(s) e nenhum cartão — nada disso vai voltar sozinho.",
                semCartoes.Materia, TipoDeSugestao.FazerCartoes));

        if (porEscrever.Count > 0 && sugestoes.Count < MaximoDeSugestoes)
            sugestoes.Add(new Sugestao(
                $"Escrever “{porEscrever[0]}”",
                "Você citou este assunto e ainda não escreveu sobre ele.",
                Materia.Nenhuma, TipoDeSugestao.Escrever));

        return sugestoes.Take(MaximoDeSugestoes).ToList();
    }

    /// <summary>
    /// "12 vencidos", "5 cartões novos", ou os dois. Dizer "vencido" para um cartão que nunca foi visto
    /// é errado e a pessoa percebe na primeira vez que abre a fila.
    /// </summary>
    private static string Motivo(MateriaNoPainel m, int novosGuardados)
    {
        var texto = (m.Vencidos, m.Ineditos) switch
        {
            ( > 0, > 0) => $"{m.Vencidos} vencido(s) e {m.Ineditos} cartão(ões) que você ainda não viu.",
            ( > 0, _) => $"{m.Vencidos} cartão(ões) vencido(s).",
            _ => $"{m.Ineditos} cartão(ões) que você ainda não viu.",
        };

        // O TETO PRECISA SER DITO AQUI, e não só no número lá em cima. Sem esta frase, o motivo promete
        // 35 cartões e a sessão entrega 2 — a pessoa clica esperando uma coisa e recebe outra, que é o
        // mesmo defeito que o número grande já tinha e que foi corrigido lá.
        if (novosGuardados > 0 && m.Ineditos > 0)
            texto += " O teto segura a maior parte para os próximos dias.";

        return texto;
    }

    private static IReadOnlyList<Atencao> Atencoes(
        IReadOnlyList<MateriaNoPainel> materias, IReadOnlyList<string> porEscrever)
    {
        var lista = new List<Atencao>();

        foreach (var m in materias.Where(m => m.Cartoes == 0 && m.Notas > 0))
            lista.Add(new Atencao(TipoDeAtencao.MateriaSemCartoes, m.Materia.Rotulo,
                $"{m.Notas} nota(s), nenhum cartão. Escrever fixa menos do que parece; sem cartão, nada volta."));

        foreach (var m in materias.Where(m => m.Facilidade is > 0 and < FacilidadeDeAtrito)
                                  .OrderBy(m => m.Facilidade))
            lista.Add(new Atencao(TipoDeAtencao.MateriaCustando, m.Materia.Rotulo,
                $"Facilidade média {m.Facilidade / 100.0:0.00}× (começa em 2,50×). Aqui você tem errado."));

        // Só as primeiras: a lista inteira de ligações quebradas pode ter dezenas, e o painel não é o
        // lugar de listá-las — é o lugar de avisar que elas existem.
        foreach (var alvo in porEscrever.Take(5))
            lista.Add(new Atencao(TipoDeAtencao.PorEscrever, alvo,
                "Citado numa nota e ainda sem nota própria."));

        return lista;
    }
}
