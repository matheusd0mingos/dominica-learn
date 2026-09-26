using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>
/// O que a pessoa FEZ: horas estudadas e questões resolvidas.
///
/// PORTA SEPARADA DO ÍNDICE, e a separação é a razão de ela existir. O índice é derivado e descartável —
/// a documentação manda apagá-lo e reindexar quando algo está estranho. Isto aqui não é reconstruível a
/// partir de nada: uma série temporal perdida está perdida, e nenhuma varredura de disco a traz de volta.
///
/// É a terceira categoria de dado do sistema, definida no docs/NORTE.md:
///
///   conhecimento → arquivo .md      (perder custa REAPRENDER)
///   registro     → banco durável    (perder custa NÃO SABER MAIS o que aconteceu)   ← este
///   derivado     → banco descartável (perder custa esperar um reindex)
///
/// Por consequência, ele TEM DE ENTRAR NO BACKUP. Sem isso, a promessa "o vault é seu" passa a valer só
/// para metade do que a pessoa construiu.
/// </summary>
public interface IRegistroDeEstudo
{
    Task RegistrarQuestoesAsync(LoteDeQuestoes lote, CancellationToken ct = default);

    Task RegistrarSessaoAsync(SessaoDeEstudo sessao, CancellationToken ct = default);

    /// <summary>
    /// Anota UMA resposta de cartão. O .md guarda só a última; a série completa — que alimenta o mapa
    /// de calor e a retenção real — só existe se cada resposta passar por aqui. Ver
    /// <see cref="RevisaoDeCartao"/>.
    /// </summary>
    Task RegistrarRevisaoAsync(RevisaoDeCartao revisao, CancellationToken ct = default);

    Task<IReadOnlyList<RevisaoDeCartao>> RevisoesAsync(DateTimeOffset desde, CancellationToken ct = default);

    /// <summary>Os lotes do período, do mais recente para o mais antigo.</summary>
    Task<IReadOnlyList<LoteDeQuestoes>> QuestoesAsync(DateTimeOffset desde, CancellationToken ct = default);

    Task<IReadOnlyList<SessaoDeEstudo>> SessoesAsync(DateTimeOffset desde, CancellationToken ct = default);

    /// <summary>
    /// Os últimos lançamentos, COM A IDENTIDADE de cada um — que é o que permite apagar o errado.
    ///
    /// POR QUE UM MÉTODO À PARTE, e não devolver a identidade em <see cref="QuestoesAsync"/>: quem calcula
    /// desempenho não deve poder apagar nada, e passar o identificador junto seria oferecer a chave a
    /// quem só precisa somar. São dois usos com poderes diferentes, e a assinatura diz qual é qual.
    /// </summary>
    Task<IReadOnlyList<LancamentoRegistrado>> UltimosAsync(int limite, CancellationToken ct = default);

    /// <summary>
    /// Apaga um lançamento. Devolve <c>false</c> quando ele não existe — ou não é de quem pediu.
    ///
    /// APAGAR, E NÃO EDITAR, é a operação que existe aqui. Um lote é imutável por construção (ver
    /// <see cref="LoteDeQuestoes"/>): "editar" seria apagar e gravar outro, e nomear isso de edição
    /// esconderia que o instante do registro muda junto. Apagar e registrar de novo faz a mesma coisa,
    /// diz a verdade sobre o que aconteceu, e é uma operação a menos para manter correta.
    /// </summary>
    Task<bool> ApagarAsync(TipoDeLancamento tipo, int id, CancellationToken ct = default);

    /// <summary>
    /// Corrige um lançamento no lugar. Devolve <c>false</c> quando ele não existe — ou não é de quem pediu.
    ///
    /// POR QUE ATUALIZAR, E NÃO "APAGAR E REGISTRAR DE NOVO" (que era a resposta antiga): a segunda
    /// forma tem uma janela em que os dois existem ou nenhum existe, dependendo da ordem — e quem
    /// corrige um "300" digitado no lugar de "30" não quer descobrir que agora tem dois lançamentos, ou
    /// nenhum. Aqui é um UPDATE: ou a linha vira a nova, ou continua a antiga.
    ///
    /// O IDENTIFICADOR NÃO MUDA, e é isso que faz a correção ser correção: a lista mostra o mesmo
    /// lançamento com outro número, no lugar de um lançamento novo no topo e um buraco onde estava o
    /// velho. A DATA também não muda — corrigir a contagem de ontem não move o estudo para hoje, que
    /// estragaria o mapa de calor e a sequência de dias.
    /// </summary>
    Task<bool> AtualizarAsync(LancamentoEditado edicao, CancellationToken ct = default);
}

/// <summary>
/// Os valores novos de um lançamento que está sendo corrigido.
///
/// UM RECORD PARA OS DOIS TIPOS, e não dois métodos: quem chama já tem o <see cref="TipoDeLancamento"/>
/// em mãos e a tela é a mesma; dois métodos fariam a tela decidir duas vezes a mesma coisa. Cada
/// implementação usa o que lhe cabe — questões ignoram <see cref="Observacao"/>, tempo ignora
/// <see cref="Total"/>, <see cref="Acertos"/> e <see cref="Fonte"/>.
/// </summary>
public sealed record LancamentoEditado(
    TipoDeLancamento Tipo,
    int Id,
    Materia Materia,
    int Total,
    int Acertos,
    TimeSpan Tempo,
    string? Fonte,
    string? Observacao);

/// <summary>Qual das duas tabelas do registro — as duas são apagáveis pelo mesmo gesto.</summary>
public enum TipoDeLancamento
{
    Questoes,
    Tempo,
}

/// <summary>
/// Um lançamento como ele aparece na lista de "o que eu registrei" — já pronto para a tela, e com o
/// identificador que o botão de apagar precisa.
/// </summary>
public sealed record LancamentoRegistrado(
    TipoDeLancamento Tipo,
    int Id,
    DateTimeOffset Em,
    Materia Materia,
    /// <summary>"40 questões · 65% de acerto" ou "50 min". O texto que descreve o lançamento.</summary>
    string Descricao,
    // —— OS VALORES CRUS, para a tela poder EDITAR ————————————————————————————————————————
    // A descrição acima é texto pronto para ler; ninguém consegue voltar dela para os números. Sem
    // estes campos, "editar" só poderia abrir um formulário em branco — e um formulário em branco não
    // é corrigir, é digitar tudo de novo, que é exatamente o atrito que faz a pessoa desistir e deixar
    // o número errado onde está.
    int Total = 0,
    int Acertos = 0,
    TimeSpan Tempo = default,
    string Fonte = "",
    string Observacao = "");
