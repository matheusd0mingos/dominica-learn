namespace Dominica.Learn.Domain.Cartoes;

/// <summary>
/// Grava o agendamento de volta no texto da nota, na linha do cartão.
///
/// É FUNÇÃO PURA — texto entra, texto sai. Isso é o que permite provar a propriedade mais importante
/// desta classe sem tocar em disco: **nada além da marca de agendamento muda**. Um cartão revisado não
/// pode reformatar o parágrafo ao lado, nem trocar o final de linha, nem mexer no frontmatter.
///
/// POR QUE ISSO É CRÍTICO AQUI, e não uma preocupação genérica de qualidade:
///
/// Gravar o agendamento MUDA O ARQUIVO, e o vigia do vault percebe qualquer mudança de arquivo. Se cada
/// revisão reescrevesse o texto além do necessário, cada cartão respondido geraria uma reconciliação de
/// nota alterada, que dispararia reindexação, que acordaria o vigia de novo. O que impede o laço é que a
/// mudança seja mínima e convirja: a segunda gravação do mesmo agendamento devolve o texto IDÊNTICO, a
/// impressão digital não muda, e a reconciliação conclui "nada mudou" e para.
///
/// Por isso <see cref="Aplicar"/> devolve a MESMA instância quando não há o que mudar — igual ao
/// <c>ReescritorDeLigacoes</c>. Não é economia de memória: é o sinal, verificável em teste, de que o
/// ciclo termina.
/// </summary>
public static class EscritorDeAgendamento
{
    /// <summary>
    /// Devolve o conteúdo com o agendamento do cartão da linha <paramref name="linha"/> atualizado.
    /// Se o texto já estiver exatamente assim, devolve o próprio <paramref name="conteudo"/>.
    /// </summary>
    public static string Aplicar(string conteudo, int linha, Agendamento agendamento)
    {
        if (string.IsNullOrEmpty(conteudo)) return conteudo;

        // Preserva o final de linha do arquivo. Trocar "\r\n" por "\n" aqui marcaria TODAS as linhas como
        // alteradas na próxima comparação — uma revisão de um cartão faria a nota inteira parecer reescrita.
        var crlf = conteudo.Contains("\r\n", StringComparison.Ordinal);
        var linhas = conteudo.Replace("\r\n", "\n").Split('\n');
        if (linha < 0 || linha >= linhas.Length) return conteudo;

        var marca = MarcaDeAgendamento.Escrever(agendamento);
        var novas = new List<string>(linhas.Length + 1);

        for (var i = 0; i < linhas.Length; i++)
        {
            if (i != linha) { novas.Add(linhas[i]); continue; }

            var atual = linhas[i];

            // 1) marca na MESMA linha: troca no lugar
            if (atual.Contains(MarcaDeAgendamento.Prefixo, StringComparison.Ordinal))
            {
                novas.Add(MarcaDeAgendamento.Remover(atual) + " " + marca);
                continue;
            }

            novas.Add(atual);

            // 2) marca na linha SEGUINTE: substitui aquela linha
            if (i + 1 < linhas.Length
                && linhas[i + 1].TrimStart().StartsWith(MarcaDeAgendamento.Prefixo, StringComparison.Ordinal))
            {
                novas.Add(marca);
                i++;   // consome a linha antiga da marca
                continue;
            }

            // 3) não havia marca: acrescenta na própria linha, separada por um espaço
            novas[^1] = atual.TrimEnd() + " " + marca;
        }

        var resultado = string.Join('\n', novas);
        if (crlf) resultado = resultado.Replace("\n", "\r\n", StringComparison.Ordinal);

        // A MESMA INSTÂNCIA quando nada mudou. É o que faz a segunda gravação do mesmo agendamento não
        // gerar arquivo novo — e, portanto, não acordar o vigia num laço.
        return string.Equals(resultado, conteudo, StringComparison.Ordinal) ? conteudo : resultado;
    }

    /// <summary>
    /// Aplica o agendamento a um cartão de bloco (pergunta / ? / resposta), onde a marca fica DEPOIS do
    /// verso e não na linha da pergunta.
    /// </summary>
    public static string AplicarEmBloco(string conteudo, Cartao cartao, Agendamento agendamento)
    {
        if (string.IsNullOrEmpty(conteudo)) return conteudo;

        var crlf = conteudo.Contains("\r\n", StringComparison.Ordinal);
        var linhas = conteudo.Replace("\r\n", "\n").Split('\n').ToList();

        var marca = MarcaDeAgendamento.Escrever(agendamento);
        var depoisDoVerso = cartao.Linha + cartao.LinhasOcupadas;

        var jaTemMarca = depoisDoVerso - 1 < linhas.Count && depoisDoVerso - 1 >= 0
            && linhas[depoisDoVerso - 1].TrimStart().StartsWith(MarcaDeAgendamento.Prefixo, StringComparison.Ordinal);

        if (jaTemMarca) linhas[depoisDoVerso - 1] = marca;
        else linhas.Insert(Math.Min(depoisDoVerso, linhas.Count), marca);

        var resultado = string.Join('\n', linhas);
        if (crlf) resultado = resultado.Replace("\n", "\r\n", StringComparison.Ordinal);
        return string.Equals(resultado, conteudo, StringComparison.Ordinal) ? conteudo : resultado;
    }
}
