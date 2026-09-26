namespace Dominica.Learn.Domain.Desempenho;

/// <summary>
/// O registro de estudo como CSV — as linhas que o backup empacota e o download direto entrega.
///
/// MORA NO DOMÍNIO porque o formato tem DOIS consumidores (o zip de backup e o endpoint /sessoes.csv), e
/// duas cópias do mesmo formato é uma delas ficando para trás — a coluna nova que aparece num arquivo e
/// não no outro, sem erro em lugar nenhum. Aqui o formato é escrito uma vez e testado uma vez.
///
/// O INSTANTE SAI NO FUSO DO CONCURSEIRO, e este parâmetro conserta um defeito que o backup carregava: o
/// banco guarda UTC (ver InstanteParaUtc), e imprimir o UTC cru punha a sessão das 21h30 de Brasília no
/// DIA SEGUINTE — numa planilha que existe justamente para conferir "quanto estudei em cada dia".
///
/// PONTO E VÍRGULA como separador, e o campo com aspas quando precisa. Vírgula seria o padrão
/// internacional e o errado aqui: o Excel em português usa ponto e vírgula, e um CSV com vírgula abre
/// com tudo numa coluna só. O arquivo existe para ser aberto, não para estar tecnicamente certo.
/// </summary>
public static class RegistroEmCsv
{
    /// <summary>As sessões de estudo, mais recente primeiro na ordem em que vierem. Primeira linha é o cabeçalho.</summary>
    public static IReadOnlyList<string> Sessoes(IEnumerable<SessaoDeEstudo> sessoes, TimeZoneInfo fuso)
    {
        var linhas = new List<string> { "inicio;materia;minutos;observacao" };
        linhas.AddRange(sessoes.Select(s =>
            $"{TimeZoneInfo.ConvertTime(s.Inicio, fuso):yyyy-MM-dd HH:mm};{Campo(s.Materia.Nome)};" +
            $"{(int)s.Duracao.TotalMinutes};{Campo(s.Observacao)}"));
        return linhas;
    }

    /// <summary>Os lotes de questões. Primeira linha é o cabeçalho.</summary>
    public static IReadOnlyList<string> Questoes(IEnumerable<LoteDeQuestoes> lotes, TimeZoneInfo fuso)
    {
        var linhas = new List<string> { "data;materia;questoes;acertos;segundos;fonte" };
        linhas.AddRange(lotes.Select(l =>
            $"{TimeZoneInfo.ConvertTime(l.Em, fuso):yyyy-MM-dd HH:mm};{Campo(l.Materia.Nome)};" +
            $"{l.Total};{l.Acertos};{(int)l.Tempo.TotalSeconds};{Campo(l.Fonte)}"));
        return linhas;
    }

    private static string Campo(string texto) =>
        texto.Contains(';') || texto.Contains('"') || texto.Contains('\n')
            ? '"' + texto.Replace("\"", "\"\"") + '"'
            : texto;
}
