using System.IO.Compression;
using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Infrastructure.Vault;

/// <summary>
/// O pacote do vault em .zip — escolhido porque qualquer sistema operacional o abre com dois cliques,
/// e a promessa "leve seu vault embora" morre se a pessoa precisar instalar algo para abri-lo.
///
/// TODA A DECISÃO DE SEGURANÇA DO IMPORT ESTÁ EM <see cref="RegrasDeImportacao"/>, no domínio. Esta
/// classe não julga nada: ela pergunta e obedece. Isso é o que permite testar os ataques (zip slip,
/// zip bomb, tipo proibido) sem criar um arquivo .zip malicioso a cada teste — e é o que impede que
/// uma refatoração do laço de extração leve junto, sem querer, a defesa.
/// </summary>
public sealed class EmpacotadorEmZip(
    RaizDoVaultDoUsuario raiz,
    IRegistroDeEstudo registro,
    IRelogio relogio,
    ILogger<EmpacotadorEmZip> log) : IEmpacotadorDoVault
{
    /// <summary>
    /// Onde o registro de estudo entra no pacote. Fora da árvore de notas, porque não é nota — e num
    /// formato que abre em qualquer planilha, porque quem baixa o backup pode querer só olhar.
    /// </summary>
    private const string PastaDoRegistro = "Registro de estudo";

    public async Task ExportarAsync(Stream destino, CancellationToken ct = default)
    {
        var pasta = await raiz.ObterAsync(ct);

        // leaveOpen: quem abriu o fluxo de resposta HTTP é o ASP.NET, e fechá-lo aqui truncaria a
        // resposta antes de os últimos bytes saírem.
        using var zip = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true);

        var arquivos = Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories);
        var quantos = 0;

        foreach (var arquivo in arquivos)
        {
            ct.ThrowIfCancellationRequested();

            // O caminho DENTRO do zip é relativo à raiz do usuário — nunca o caminho absoluto do
            // servidor, que revelaria a estrutura de pastas da máquina a quem abrir o pacote.
            var relativo = Path.GetRelativePath(pasta, arquivo).Replace('\\', '/');

            // Arquivo temporário de editor, lixo do sistema de arquivos: não é conhecimento e só
            // atrapalharia quem abrir o pacote do outro lado.
            if (relativo.StartsWith('.') || relativo.Contains("/.", StringComparison.Ordinal)) continue;

            var entrada = zip.CreateEntry(relativo, CompressionLevel.Optimal);
            entrada.LastWriteTime = File.GetLastWriteTimeUtc(arquivo);

            await using var origem = File.OpenRead(arquivo);
            await using var saida = entrada.Open();
            await origem.CopyToAsync(saida, ct);
            quantos++;
        }

        // O REGISTRO VAI JUNTO, e isto não é extra: é o que mantém a promessa de pé. Horas estudadas e
        // questões resolvidas não estão em arquivo nenhum do vault — moram num banco durável — e um
        // backup que só leva os .md deixaria metade do que a pessoa construiu no servidor. "O vault é
        // seu" viraria meia verdade, que é o tipo de promessa que só se descobre quebrada na hora de sair.
        //
        // CSV, e não o formato interno do banco: quem abre o pacote precisa conseguir LER, e uma planilha
        // é o menor denominador comum que existe. Ver docs/NORTE.md.
        await ExportarRegistroAsync(zip, ct);

        log.LogInformation("Vault exportado: {Quantos} arquivo(s), mais o registro de estudo.", quantos);
    }

    private async Task ExportarRegistroAsync(ZipArchive zip, CancellationToken ct)
    {
        var desdeSempre = DateTimeOffset.MinValue;

        var lotes = await registro.QuestoesAsync(desdeSempre, ct);
        if (lotes.Count > 0)
        {
            var linhas = new List<string> { "data;materia;questoes;acertos;segundos;fonte" };
            linhas.AddRange(lotes.Select(l =>
                $"{l.Em:yyyy-MM-dd HH:mm};{Campo(l.Materia.Nome)};{l.Total};{l.Acertos};" +
                $"{(int)l.Tempo.TotalSeconds};{Campo(l.Fonte)}"));
            await EscreverAsync(zip, $"{PastaDoRegistro}/questoes.csv", linhas, ct);
        }

        var sessoes = await registro.SessoesAsync(desdeSempre, ct);
        if (sessoes.Count > 0)
        {
            var linhas = new List<string> { "inicio;materia;minutos;observacao" };
            linhas.AddRange(sessoes.Select(s =>
                $"{s.Inicio:yyyy-MM-dd HH:mm};{Campo(s.Materia.Nome)};" +
                $"{(int)s.Duracao.TotalMinutes};{Campo(s.Observacao)}"));
            await EscreverAsync(zip, $"{PastaDoRegistro}/sessoes.csv", linhas, ct);
        }
    }

    /// <summary>
    /// PONTO E VÍRGULA como separador, e o campo com aspas quando precisa.
    ///
    /// Vírgula seria o padrão internacional e o errado aqui: o Excel em português usa ponto e vírgula, e
    /// um CSV com vírgula abre com tudo numa coluna só. O backup existe para ser aberto, não para estar
    /// tecnicamente certo.
    /// </summary>
    private static string Campo(string texto) =>
        texto.Contains(';') || texto.Contains('"') || texto.Contains('\n')
            ? '"' + texto.Replace("\"", "\"\"") + '"'
            : texto;

    private static async Task EscreverAsync(ZipArchive zip, string nome, IEnumerable<string> linhas, CancellationToken ct)
    {
        var entrada = zip.CreateEntry(nome, CompressionLevel.Optimal);
        await using var saida = entrada.Open();
        // BOM em UTF-8: sem ele o Excel lê "Direito tributÃ¡rio". É o mesmo detalhe de sempre, e é o que
        // separa um arquivo que abre de um arquivo que abre errado.
        await using var texto = new StreamWriter(saida, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        foreach (var linha in linhas)
        {
            ct.ThrowIfCancellationRequested();
            await texto.WriteLineAsync(linha);
        }
    }

    public async Task<ResultadoDaImportacao> ImportarAsync(Stream pacote, CancellationToken ct = default)
    {
        var pasta = await raiz.ObterAsync(ct);
        var chegada = RegrasDeImportacao.PastaDeChegada(DateOnly.FromDateTime(relogio.Agora.LocalDateTime));

        using var zip = new ZipArchive(pacote, ZipArchiveMode.Read, leaveOpen: true);

        // JULGA TUDO ANTES DE GRAVAR QUALQUER COISA. Julgar e gravar no mesmo laço deixaria metade de
        // uma bomba no disco antes de o teto ser atingido — e o teto do pacote só faz sentido se for
        // decidido sobre o pacote inteiro.
        var julgadas = RegrasDeImportacao.Julgar(
            zip.Entries.Select(e => (e.FullName, e.Length)), chegada);

        var porNome = zip.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
        var aceitas = 0;

        foreach (var julgada in julgadas.Where(j => j.Aceita))
        {
            ct.ThrowIfCancellationRequested();
            if (!porNome.TryGetValue(julgada.NomeNoPacote, out var entrada)) continue;

            // CaminhoSeguro é a segunda linha: o destino já passou pelas regras do domínio, mas quem
            // grava confere de novo, resolvendo symlink segmento a segmento. Uma pasta do vault que
            // seja um link simbólico para fora escaparia de qualquer validação puramente textual.
            var destino = CaminhoSeguro.Combinar(pasta, julgada.Destino!, "arquivo importado");

            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

            // Mode.CreateNew: se o arquivo já existe, ALGO deu errado — a pasta de chegada carrega a
            // data justamente para não colidir. Falhar aqui é melhor que sobrescrever calado.
            await using var saida = new FileStream(
                destino, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var origem = entrada.Open();
            await origem.CopyToAsync(saida, ct);
            aceitas++;
        }

        var recusadas = julgadas.Where(j => !j.Aceita && j.Recusa != MotivoDaRecusa.NomeInvalido).ToList();
        log.LogInformation("Vault importado em {Pasta}: {Aceitas} aceito(s), {Recusadas} recusado(s).",
            chegada, aceitas, recusadas.Count);

        return new ResultadoDaImportacao(aceitas, recusadas, chegada);
    }
}
