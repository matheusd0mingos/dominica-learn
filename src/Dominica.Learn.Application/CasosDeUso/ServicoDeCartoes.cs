using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

/// <summary>Quantos cartões vencidos uma matéria tem hoje — o número que decide por onde começar.</summary>
public sealed record CartoesDaMateria(Materia Materia, int Vencidos, int Total);

/// <summary>
/// A fila de hoje, e o que o teto diário segurou.
///
/// <see cref="IneditosSegurados"/> é mostrado na tela de propósito: sem ele, quem escreveu 37 cartões
/// veria 20 e concluiria que os outros se perderam. Um limite que age em silêncio vira defeito aos olhos
/// de quem o sofre, mesmo estando certo.
/// </summary>
public sealed record FilaDeRevisao(IReadOnlyList<Cartao> Cartoes, int IneditosSegurados, int Teto)
{
    public static readonly FilaDeRevisao Vazia = new([], 0, 0);
}

/// <summary>
/// A fila de revisão e a resposta a um cartão.
///
/// O SERVIÇO NÃO GUARDA ESTADO DE SESSÃO. A fila é recalculada a partir dos arquivos toda vez que se
/// pede, e a resposta grava direto no arquivo. Guardar a sessão em memória seria mais rápido e perderia
/// a revisão de quem fechou o navegador no meio — que numa rotina de estudo diária acontece toda semana.
/// </summary>
public sealed class ServicoDeCartoes(
    IRepositorioDeNotas repositorio,
    IIndiceDoVault indice,
    ServicoDeNotas notas,
    IPreferenciasDoUsuario preferencias,
    IRelogio relogio,
    ILogger<ServicoDeCartoes> log)
{
    private DateOnly Hoje => DateOnly.FromDateTime(relogio.Agora.ToLocalTime().DateTime);

    /// <summary>
    /// Os cartões vencidos, prontos para revisar. <paramref name="materia"/> nulo = o vault inteiro.
    ///
    /// A ORDEM É DETERMINÍSTICA e INTERCALADA — ver <see cref="OrdemDaFila"/>. Os mais atrasados
    /// primeiro; dentro de uma mesma data, um cartão de cada nota por rodada, para que a sessão não vire
    /// um bloco de cartões da mesma nota (onde a pessoa lembra do que leu, não do conceito).
    /// </summary>
    public async Task<FilaDeRevisao> FilaAsync(
        Materia? materia = null, int limite = 100, CancellationToken ct = default)
    {
        var hoje = Hoje;
        var fila = new List<Cartao>();

        // TODOS os cartões, e não só os vencidos: o teto diário precisa saber quantos inéditos JÁ foram
        // respondidos hoje, e esses saíram da fila justamente por terem sido respondidos.
        var todos = new List<Cartao>();

        foreach (var caminho in await CaminhosAsync(materia, ct))
        {
            var nota = await repositorio.LerAsync(caminho, ct);
            if (nota is null) continue;

            foreach (var cartao in AnalisadorDeCartoes.Analisar(caminho, nota.Conteudo))
            {
                // O suspenso fica FORA da fila e fora da conta do teto: ele não é dívida de revisão nem
                // consome cota — está guardado, não adiado.
                if (cartao.Suspenso) continue;

                todos.Add(cartao);
                if (cartao.AgendamentoOu(hoje).Vencido(hoje)) fila.Add(cartao);
            }
        }

        var teto = await preferencias.CartoesNovosPorDiaAsync(ct);
        var ordenada = OrdemDaFila.Intercalar(fila, hoje);

        // O TETO VEM DEPOIS DA INTERCALAÇÃO, e não antes: cortando primeiro, os inéditos escolhidos
        // sairiam da ordem por caminho — todos da mesma nota — e a intercalação não teria mais o que
        // intercalar.
        var comTeto = TetoDeCartoesNovos.Aplicar(ordenada, todos, hoje, teto);

        var segurados = ordenada.Count(TetoDeCartoesNovos.EhInedito)
                        - comTeto.Count(TetoDeCartoesNovos.EhInedito);

        return new FilaDeRevisao(comTeto.Take(limite).ToList(), segurados, teto);
    }

    /// <summary>
    /// A nota onde os cartões nascidos de erro se acumulam, por matéria.
    ///
    /// UMA NOTA POR MATÉRIA, e não uma só para o vault inteiro: o erro pertence à matéria, e é lá que
    /// ele precisa aparecer no grafo, na contagem e na revisão por matéria. Uma nota geral de erros
    /// viraria um depósito que ninguém relê.
    /// </summary>
    public const string NotaDeErros = "Errei na prova";

    /// <summary>
    /// Grava um cartão nascido de uma questão errada, na nota de erros da matéria — criando-a se preciso.
    ///
    /// POR QUE ISTO MERECE UM CAMINHO PRÓPRIO, em vez de "abra a nota certa e use o botão de cartão":
    ///
    /// Errar uma questão é o momento de maior valor do estudo inteiro, e é também o momento de menor
    /// paciência — a pessoa está no meio de um simulado, com trinta questões pela frente. Qualquer
    /// passo a mais entre "errei isso" e "está gravado" é o passo em que ela desiste, e o erro se perde.
    /// Por isso o gesto é: escolher a matéria, escrever pergunta e resposta, pronto.
    ///
    /// A ETIQUETA VAI NA NOTA, não no cartão: assim "#errei-na-prova" atravessa as matérias no painel de
    /// etiquetas e responde "onde eu mais erro?" — que é a pergunta que faz o registro valer a pena.
    /// </summary>
    public async Task<Resultado<CaminhoNota>> RegistrarErroAsync(
        Materia materia, string textoDoCartao, CancellationToken ct = default)
    {
        if (materia is null || !materia.Existe)
            return Resultado<CaminhoNota>.Falha(MotivoDaFalha.Invalida, "Escolha a matéria.");
        if (string.IsNullOrWhiteSpace(textoDoCartao))
            return Resultado<CaminhoNota>.Falha(MotivoDaFalha.Invalida, "O cartão está vazio.");

        if (!CaminhoNota.TentarCriar($"{materia.Nome}/{NotaDeErros}.md", out var caminho, out var erro) || caminho is null)
            return Resultado<CaminhoNota>.Falha(MotivoDaFalha.Invalida, erro ?? "Caminho inválido.");

        var nota = await repositorio.LerAsync(caminho, ct);

        if (nota is null)
        {
            var inicial = $"# {NotaDeErros} — {materia.Rotulo}\n\n#errei-na-prova\n\n" +
                          "Cada cartão aqui nasceu de uma questão errada. O que erra não costuma ser o\n" +
                          "conceito: é a confusão entre dois conceitos parecidos.\n\n" +
                          textoDoCartao.TrimEnd() + "\n";

            var criada = await notas.CriarAsync(caminho, inicial, ct);
            return criada.Ok
                ? Resultado<CaminhoNota>.Sucesso(caminho)
                : Resultado<CaminhoNota>.Falha(criada.Motivo ?? MotivoDaFalha.Invalida,
                    criada.Mensagem ?? "Não consegui criar a nota de erros.");
        }

        // ACRESCENTA NO FIM, com linha em branco antes: colar o cartão no final do parágrafo anterior o
        // desfaria — o "::" passaria a dividir o texto que já estava lá.
        var conteudo = nota.Conteudo.TrimEnd() + "\n\n" + textoDoCartao.TrimEnd() + "\n";

        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        return salva.Ok
            ? Resultado<CaminhoNota>.Sucesso(caminho)
            : Resultado<CaminhoNota>.Falha(salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Mensagem ?? "Não consegui gravar o erro.");
    }

    /// <summary>Quantos cartões cada matéria tem vencidos. É o painel do "por onde começo hoje".</summary>
    public async Task<IReadOnlyList<CartoesDaMateria>> PorMateriaAsync(CancellationToken ct = default)
    {
        var hoje = Hoje;
        var contagem = new Dictionary<Materia, (int Vencidos, int Total)>();

        foreach (var caminho in await CaminhosAsync(null, ct))
        {
            var nota = await repositorio.LerAsync(caminho, ct);
            if (nota is null) continue;

            var cartoes = AnalisadorDeCartoes.Analisar(caminho, nota.Conteudo);
            if (cartoes.Count == 0) continue;

            var materia = Materia.De(caminho);
            var atual = contagem.GetValueOrDefault(materia);
            contagem[materia] = (
                atual.Vencidos + cartoes.Count(c => c.AgendamentoOu(hoje).Vencido(hoje)),
                atual.Total + cartoes.Count);
        }

        return contagem
            .Select(p => new CartoesDaMateria(p.Key, p.Value.Vencidos, p.Value.Total))
            .OrderByDescending(m => m.Vencidos)
            .ThenBy(m => m.Materia.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Responde um cartão: calcula o próximo agendamento e grava no arquivo.
    ///
    /// A GRAVAÇÃO PASSA PELO ServicoDeNotas, não direto no repositório — e essa é a decisão que resolve o
    /// segundo problema do agendamento em arquivo. O serviço de notas é quem detecta conflito por
    /// impressão digital, e é justamente esse mecanismo que impede o cenário ruim: a nota aberta noutra
    /// aba, com autosave pendente, sobrescrevendo a revisão que acabou de ser gravada aqui.
    ///
    /// O cartão é RELIDO do disco antes de agendar. O que estava na tela pode ter minutos de idade — e
    /// agendar sobre uma leitura velha reescreveria por cima de uma revisão feita no Obsidian nesse meio
    /// tempo.
    /// </summary>
    public async Task<Resultado<Agendamento>> ResponderAsync(
        CaminhoNota caminho, int linha, Resposta resposta, CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado<Agendamento>.NaoEncontrada($"A nota \"{caminho}\"");

        var cartao = AnalisadorDeCartoes.Analisar(caminho, nota.Conteudo).FirstOrDefault(c => c.Linha == linha);
        if (cartao is null)
            // A nota mudou por fora e o cartão não está mais naquela linha. Recusar é o certo: agendar
            // pela posição levaria o agendamento para o cartão errado, em silêncio.
            return Resultado<Agendamento>.NaoEncontrada($"O cartão na linha {linha + 1} de \"{caminho}\"");

        var hoje = Hoje;
        var agendamento = AgendadorSM2.Proximo(cartao.AgendamentoOu(hoje), resposta, hoje);

        var conteudo = EhDeBloco(nota.Conteudo, cartao)
            ? EscritorDeAgendamento.AplicarEmBloco(nota.Conteudo, cartao, agendamento)
            : EscritorDeAgendamento.Aplicar(nota.Conteudo, cartao.Linha, agendamento);

        // Nada mudou: o mesmo agendamento já estava gravado. Não gravar é o que impede o vigia de
        // acordar à toa — e o que garante que o ciclo revisar→gravar→reconciliar termine.
        if (ReferenceEquals(conteudo, nota.Conteudo)) return Resultado<Agendamento>.Sucesso(agendamento);

        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        if (!salva.Ok)
            return Resultado<Agendamento>.Falha(
                salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Motivo == MotivoDaFalha.Conflito
                    ? "A nota mudou enquanto você revisava. Abra o cartão de novo para não gravar por cima."
                    : salva.Mensagem ?? "Não consegui gravar a revisão.");

        log.LogInformation("Cartão de {Nota}:{Linha} revisado ({Resposta}); volta em {Dias} dia(s).",
            caminho, cartao.Linha + 1, resposta, agendamento.IntervaloEmDias);

        return Resultado<Agendamento>.Sucesso(agendamento);
    }

    /// <summary>
    /// Suspende ou devolve um cartão à fila.
    ///
    /// SUSPENDER NÃO É APAGAR, e a diferença aparece no agendamento: ele fica intacto no arquivo, então
    /// devolver o cartão à fila devolve junto o histórico que ele tinha. Ver <see cref="Suspensao"/>.
    ///
    /// RELÊ O CARTÃO DO DISCO antes de mexer, como o responder faz: o que estava na tela pode ter minutos
    /// de idade, e suspender pela posição antiga suspenderia o cartão vizinho — em silêncio.
    /// </summary>
    public async Task<Resultado<bool>> SuspenderAsync(
        CaminhoNota caminho, int linha, bool suspenso, CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado<bool>.NaoEncontrada($"A nota \"{caminho}\"");

        var cartao = AnalisadorDeCartoes.Analisar(caminho, nota.Conteudo).FirstOrDefault(c => c.Linha == linha);
        if (cartao is null)
            return Resultado<bool>.NaoEncontrada($"O cartão na linha {linha + 1} de \"{caminho}\"");

        var conteudo = Suspensao.Definir(nota.Conteudo, cartao.Linha, suspenso);
        if (ReferenceEquals(conteudo, nota.Conteudo)) return Resultado<bool>.Sucesso(suspenso);

        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        if (!salva.Ok)
            return Resultado<bool>.Falha(
                salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Mensagem ?? "Não consegui gravar.");

        log.LogInformation("Cartão de {Nota}:{Linha} {Estado}.",
            caminho, cartao.Linha + 1, suspenso ? "suspenso" : "devolvido à fila");

        return Resultado<bool>.Sucesso(suspenso);
    }

    /// <summary>
    /// Cartão de bloco tem a marca DEPOIS do verso; o de uma linha, na própria linha. Distinguir pelo
    /// separador é mais confiável que pelo tamanho: um cartão de bloco de uma linha só existe.
    /// </summary>
    private static bool EhDeBloco(string conteudo, Cartao cartao)
    {
        var linhas = conteudo.Replace("\r\n", "\n").Split('\n');
        var seguinte = cartao.Linha + 1;
        return seguinte < linhas.Length && linhas[seguinte].Trim() is "?" or "??";
    }

    private async Task<IReadOnlyList<CaminhoNota>> CaminhosAsync(Materia? materia, CancellationToken ct)
    {
        var todos = await indice.TodosOsCaminhosAsync(ct);
        return materia is null ? todos : todos.Where(c => Materia.De(c) == materia).ToList();
    }
}
