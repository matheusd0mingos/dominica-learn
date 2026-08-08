using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Cartoes;
using Dominica.Learn.Domain.Desempenho;
using Dominica.Learn.Domain.Painel;
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
public sealed record FilaDeRevisao(
    IReadOnlyList<Cartao> Cartoes,
    int IneditosSegurados,
    int Teto,
    /// <summary>
    /// Por que esta ordem, quando o registro de estudo teve algo a dizer sobre ela. Nulo quando a ordem
    /// é a de sempre. Fila reordenada em silêncio é pior que fila não reordenada — ver OndeVocePerde.
    /// </summary>
    string? MotivoDaOrdem = null)
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
    // O REGISTRO DE ESTUDO ENTRA AQUI, e esta linha é a junção das duas metades do produto: a fila
    // deixa de ser decidida só pelo que os cartões sabem (atraso, facilidade) e passa a ouvir onde a
    // pessoa erra QUESTÃO DE PROVA. Ver OndeVocePerde.
    IRegistroDeEstudo registro,
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
        Materia? materia = null, Etiqueta? etiqueta = null, Etiqueta? etiqueta2 = null,
        int limite = 100, CancellationToken ct = default)
    {
        var hoje = Hoje;
        var fila = new List<Cartao>();

        // POR ETIQUETA, além de por matéria — e isto fecha um ciclo que estava pela metade: o produto
        // CRIA "#errei-na-prova" sozinho a cada erro registrado, mas não havia como dizer "hoje só
        // reviso o que errei". A busca do índice já entende a hierarquia (#direito traz #direito/penal),
        // então a mesma pergunta que o painel de etiquetas responde é a que recorta a fila.
        //
        // DUAS ETIQUETAS = INTERSEÇÃO, não união. É o recorte que a linha do mapa oferece — "o que eu
        // errei E é pegadinha" — e a interseção é o que faz duas etiquetas dizerem mais que uma: a união
        // diria menos.
        HashSet<CaminhoNota>? comEtiqueta = null;
        foreach (var e in new[] { etiqueta, etiqueta2 }.Where(e => e is not null))
        {
            var acertos = await indice.BuscarAsync(
                new ConsultaDeBusca { Etiqueta = e, Limite = int.MaxValue }, ct);
            var caminhos = acertos.Select(a => a.Nota.Caminho).ToHashSet();
            if (comEtiqueta is null) comEtiqueta = caminhos;
            else comEtiqueta.IntersectWith(caminhos);
        }

        // TODOS os cartões, e não só os vencidos: o teto diário precisa saber quantos inéditos JÁ foram
        // respondidos hoje, e esses saíram da fila justamente por terem sido respondidos.
        var todos = new List<Cartao>();

        foreach (var caminho in await CaminhosAsync(materia, ct))
        {
            if (comEtiqueta is not null && !comEtiqueta.Contains(caminho)) continue;
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

        // O DESEMPENHO SÓ DESEMPATA DENTRO DO MESMO DIA DE VENCIMENTO — nunca por cima do atraso. Ver
        // OrdemDaFila.Intercalar. Quando a revisão já está filtrada por matéria, não há o que priorizar
        // entre matérias, e a consulta ao registro seria trabalho jogado fora.
        var desempenho = materia is null ? await ResumoDoRegistroAsync(ct) : null;
        var ordenada = OrdemDaFila.Intercalar(fila, hoje, OndeVocePerde.Prioridades(desempenho));

        // O TETO VEM DEPOIS DA INTERCALAÇÃO, e não antes: cortando primeiro, os inéditos escolhidos
        // sairiam da ordem por caminho — todos da mesma nota — e a intercalação não teria mais o que
        // intercalar.
        var comTeto = TetoDeCartoesNovos.Aplicar(ordenada, todos, hoje, teto);

        var segurados = ordenada.Count(TetoDeCartoesNovos.EhInedito)
                        - comTeto.Count(TetoDeCartoesNovos.EhInedito);

        // O motivo só é dito quando alguma coisa DE FATO mudou de lugar. Explicar uma reordenação que
        // não houve é ruído — e ruído numa tela de revisão é o que faz parar de ler os avisos dela.
        var motivo = ordenada.Count > 1 ? OndeVocePerde.Explicar(desempenho) : null;

        return new FilaDeRevisao(comTeto.Take(limite).ToList(), segurados, teto, motivo);
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

    /// <summary>
    /// Acrescenta um cartão ao fim de uma nota que JÁ EXISTE.
    ///
    /// POR QUE ELE É SEPARADO DO <see cref="RegistrarErroAsync"/>, que faz quase a mesma coisa: aquele
    /// tem uma intenção embutida — o cartão nasceu de uma questão errada, vai para a nota de erros da
    /// matéria, e leva a etiqueta que responde "onde eu mais erro". Este não tem intenção nenhuma: é
    /// "quero um cartão sobre isto, nesta nota". Fundir os dois faria todo cartão criado às pressas
    /// virar registro de erro, e a etiqueta #errei-na-prova deixaria de significar o que significa.
    ///
    /// NÃO CRIA NOTA. Criar nota é um gesto com consequência — ela aparece no grafo, na contagem, na
    /// busca — e não pode acontecer como efeito colateral de "criar um cartão". Nota inexistente é recusa
    /// com o nome dela na mensagem.
    /// </summary>
    public async Task<Resultado<CaminhoNota>> AcrescentarCartaoAsync(
        CaminhoNota caminho, string textoDoCartao, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(textoDoCartao))
            return Resultado<CaminhoNota>.Falha(MotivoDaFalha.Invalida, "O cartão está vazio.");

        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado<CaminhoNota>.NaoEncontrada($"A nota \"{caminho}\"");

        // Linha em branco antes, pelo mesmo motivo do registro de erro: colado no fim do parágrafo
        // anterior, o "::" passaria a dividir o texto que já estava lá.
        var conteudo = nota.Conteudo.TrimEnd() + "\n\n" + textoDoCartao.TrimEnd() + "\n";

        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        if (!salva.Ok)
            return Resultado<CaminhoNota>.Falha(salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Mensagem ?? "Não consegui gravar o cartão.");

        log.LogInformation("Cartão acrescentado em {Nota}.", caminho);
        return Resultado<CaminhoNota>.Sucesso(caminho);
    }

    /// <summary>
    /// A foto do estudo para o painel.
    ///
    /// LÊ O VAULT INTEIRO uma vez, e não uma consulta por número mostrado. São dezenas de arquivos
    /// pequenos; a alternativa — uma varredura por indicador — multiplicaria a leitura por cinco para
    /// produzir a mesma foto, e ainda correria o risco de dois números da mesma tela discordarem por
    /// terem sido lidos em momentos diferentes.
    ///
    /// "Por escrever" são as LIGAÇÕES QUEBRADAS do vault: o que a pessoa citou entre [[colchetes]] e
    /// ainda não escreveu. Não é erro a corrigir — é a lista do que ela mesma decidiu estudar.
    /// </summary>
    public async Task<RetratoDoEstudo> PainelAsync(CancellationToken ct = default)
    {
        var hoje = Hoje;
        var cartoes = new List<Cartao>();
        var notasPorMateria = new Dictionary<Materia, int>();
        var referencias = new HashSet<Materia>();

        foreach (var caminho in await CaminhosAsync(materia: null, ct))
        {
            var nota = await repositorio.LerAsync(caminho, ct);
            if (nota is null) continue;

            var m = Materia.De(caminho);
            notasPorMateria[m] = notasPorMateria.GetValueOrDefault(m) + 1;
            cartoes.AddRange(AnalisadorDeCartoes.Analisar(caminho, nota.Conteudo));

            // A MARCA DE REFERÊNCIA SÓ VALE NA NOTA-ÍNDICE DA MATÉRIA — "Trabalho/Trabalho.md". Aceitá-la
            // em qualquer nota faria uma anotação solta desligar a cobrança da matéria inteira, e a
            // pessoa nunca descobriria qual arquivo fez isso.
            //
            // Não custa leitura nenhuma: este laço já abriu todas as notas para procurar cartões.
            if (m.Existe && ServicoDeConhecimento.EhNotaIndiceDaMateria(caminho)
                && EditorDeFrontmatter.EhReferencia(nota.Analise))
                referencias.Add(m);
        }

        // Distinct pelo ALVO: citar "[[Pregão]]" em cinco notas é UM assunto por escrever, não cinco.
        var porEscrever = new List<string>();
        foreach (var caminho in await indice.TodosOsCaminhosAsync(ct))
            foreach (var l in await indice.LigacoesDeAsync(caminho, ct))
                if (l.Quebrada && !porEscrever.Contains(l.Alvo, StringComparer.OrdinalIgnoreCase))
                    porEscrever.Add(l.Alvo);

        var teto = await preferencias.CartoesNovosPorDiaAsync(ct);
        return PainelDeEstudo.Montar(cartoes, notasPorMateria, porEscrever, hoje, teto, referencias);
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

        // A RESPOSTA VAI PARA O REGISTRO — é o que alimenta o mapa de calor e a retenção real, porque o
        // .md só guarda a última. NUNCA derruba a revisão: a revisão já está gravada no arquivo, que é a
        // verdade; o registro é a série histórica, e perder um ponto dela vale um aviso no log, não um
        // erro na cara de quem está no meio da fila.
        try
        {
            await registro.RegistrarRevisaoAsync(
                new RevisaoDeCartao(relogio.Agora, Materia.De(caminho), resposta), ct);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "A revisão de {Nota}:{Linha} foi gravada, mas não entrou no registro.",
                caminho, cartao.Linha + 1);
        }

        return Resultado<Agendamento>.Sucesso(agendamento);
    }

    /// <summary>
    /// Desfaz a última resposta, devolvendo o cartão ao agendamento que ele tinha antes.
    ///
    /// POR QUE ISTO PRECISA EXISTIR: responder é a única ação irreversível do ciclo diário, e ela é feita
    /// no celular, com o polegar, em sequência. Tocar "Fácil" num cartão que se errou o tira da frente
    /// por semanas — e o estrago não aparece hoje, aparece na prova. Sem desfazer, a saída da pessoa é
    /// abrir o .md e editar a marca à mão, o que ninguém faz; o que se faz é conviver com o erro.
    ///
    /// <paramref name="anterior"/> NULO NÃO É "sem informação": é a informação de que o cartão era
    /// INÉDITO. Nesse caso desfazer APAGA a marca, em vez de escrever alguma. Reagendar um inédito para
    /// amanhã seria inventar um terceiro estado que nunca existiu — e o cartão deixaria de contar como
    /// novo para o teto diário, para sempre. Ver EscritorDeAgendamento.Remover.
    ///
    /// RELÊ DO DISCO, como o responder: entre responder e desfazer podem ter passado segundos em que o
    /// arquivo mudou (o Obsidian aberto do outro lado, por exemplo). Desfazer pela posição antiga
    /// mexeria no cartão vizinho, em silêncio.
    /// </summary>
    public async Task<Resultado<int>> DesfazerRespostaAsync(
        CaminhoNota caminho, int linha, Agendamento? anterior, CancellationToken ct = default)
    {
        var nota = await repositorio.LerAsync(caminho, ct);
        if (nota is null) return Resultado<int>.NaoEncontrada($"A nota \"{caminho}\"");

        var cartao = AnalisadorDeCartoes.Analisar(caminho, nota.Conteudo).FirstOrDefault(c => c.Linha == linha);
        if (cartao is null)
            return Resultado<int>.NaoEncontrada($"O cartão na linha {linha + 1} de \"{caminho}\"");

        var conteudo = anterior is null
            ? EscritorDeAgendamento.Remover(nota.Conteudo, cartao)
            : EhDeBloco(nota.Conteudo, cartao)
                ? EscritorDeAgendamento.AplicarEmBloco(nota.Conteudo, cartao, anterior)
                : EscritorDeAgendamento.Aplicar(nota.Conteudo, cartao.Linha, anterior);

        // Já estava assim — não gravar é o que impede o vigia de acordar à toa.
        if (ReferenceEquals(conteudo, nota.Conteudo)) return Resultado<int>.Sucesso(0);

        var salva = await notas.SalvarAsync(caminho, conteudo, nota.Impressao, autor: null, ct);
        if (!salva.Ok)
            return Resultado<int>.Falha(
                salva.Motivo ?? MotivoDaFalha.Invalida,
                salva.Motivo == MotivoDaFalha.Conflito
                    ? "A nota mudou depois que você respondeu. Não desfiz, para não gravar por cima."
                    : salva.Mensagem ?? "Não consegui desfazer.");

        log.LogInformation("Resposta desfeita em {Nota}:{Linha}.", caminho, cartao.Linha + 1);
        return Resultado<int>.Sucesso(1);
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
    /// <summary>
    /// Todos os cartões suspensos do vault — a lista que faz o suspender ter volta.
    ///
    /// POR QUE ISTO PRECISA EXISTIR: suspender era porta sem volta — o botão existia, e nenhuma tela
    /// listava os suspensos nem os devolvia. Cartão que some para sempre com um clique não é "guardado",
    /// é apagado com outro nome; a diferença entre os dois é ESTA consulta.
    /// </summary>
    public async Task<IReadOnlyList<Cartao>> SuspensosAsync(CancellationToken ct = default)
    {
        var lista = new List<Cartao>();
        foreach (var caminho in await CaminhosAsync(null, ct))
        {
            var nota = await repositorio.LerAsync(caminho, ct);
            if (nota is null) continue;
            lista.AddRange(AnalisadorDeCartoes.Analisar(caminho, nota.Conteudo).Where(c => c.Suspenso));
        }
        return [.. lista
            .OrderBy(c => c.Nota.Valor, StringComparer.Ordinal)
            .ThenBy(c => c.Linha)];
    }

    /// <summary>
    /// Todos os cartões do vault no formato de importação do Anki. Ver <see cref="ExportadorDeAnki"/> —
    /// inclusive por que é texto e não .apkg.
    /// </summary>
    public async Task<string> ExportarParaAnkiAsync(CancellationToken ct = default)
    {
        var todos = new List<Cartao>();
        foreach (var caminho in await CaminhosAsync(null, ct))
        {
            var nota = await repositorio.LerAsync(caminho, ct);
            if (nota is null) continue;
            todos.AddRange(AnalisadorDeCartoes.Analisar(caminho, nota.Conteudo));
        }
        return ExportadorDeAnki.Exportar([.. todos
            .OrderBy(c => c.Nota.Valor, StringComparer.Ordinal)
            .ThenBy(c => c.Linha)]);
    }

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
    /// <summary>
    /// O desempenho da janela padrão, para decidir a ordem da fila.
    ///
    /// ENGOLE A FALHA DE PROPÓSITO. O registro mora noutro banco (ver ContextoDoRegistro): se ele estiver
    /// fora do ar, a revisão TEM DE CONTINUAR — ela é o coração do produto e depende só dos arquivos.
    /// Sem esta guarda, um banco de registro indisponível derrubaria a tela de revisar por causa de um
    /// desempate. O custo do silêncio é a fila voltar à ordem de sempre, que é a ordem correta que ela
    /// tinha antes deste recurso existir.
    /// </summary>
    private async Task<ResumoDeDesempenho?> ResumoDoRegistroAsync(CancellationToken ct)
    {
        try
        {
            var desde = relogio.Agora - ServicoDeDesempenho.JanelaPadrao;
            return CalculoDeDesempenho.Montar(
                await registro.QuestoesAsync(desde, ct), await registro.SessoesAsync(desde, ct));
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Não consegui ler o registro de estudo; a fila vai na ordem de sempre.");
            return null;
        }
    }

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
