using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Anexos;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

/// <summary>Um anexo que acabou de entrar no vault, com o Markdown pronto para colar na nota.</summary>
public sealed record AnexoGuardado(CaminhoDeAnexo Caminho, string Markdown);

/// <summary>
/// Anexar arquivos ao vault.
///
/// A DECISÃO QUE VALE EXPLICAR — o Markdown gerado é <c>![[Anexos/foto.png]]</c>, um EMBED do Obsidian,
/// e não <c>![foto](/anexos/foto.png)</c>, que seria mais simples de renderizar aqui.
///
/// O motivo é a promessa central do produto: a mesma pasta abre no Obsidian Desktop. Uma URL da aplicação
/// dentro do arquivo .md renderiza aqui e mostra um link morto lá — e no dia em que este servidor sair do
/// ar, a nota fica com uma referência para um endereço que não existe mais. O embed é relativo ao vault,
/// então vale nas duas ferramentas e continua valendo sem servidor nenhum.
///
/// O preço é que o renderizador precisa traduzir o embed para uma URL na hora de exibir. É trabalho de
/// APRESENTAÇÃO, que é onde ele deve ficar — e não uma marca deixada dentro do conteúdo do usuário.
/// </summary>
public sealed class ServicoDeAnexos(IArmazemDeAnexos armazem, ILogger<ServicoDeAnexos> log)
{
    /// <summary>
    /// Limite por arquivo. Existe para que um vídeo arrastado por engano não encha o disco do VPS antes
    /// de alguém perceber; é configurável porque "grande demais" depende do disco de quem hospeda.
    /// </summary>
    public const long TamanhoMaximoPadraoBytes = 25 * 1024 * 1024;

    public async Task<Resultado<AnexoGuardado>> AnexarAsync(
        string nomeOriginal, Stream conteudo, long tamanhoBytes,
        long tamanhoMaximoBytes = TamanhoMaximoPadraoBytes, CancellationToken ct = default)
    {
        if (tamanhoBytes <= 0)
            return Resultado<AnexoGuardado>.Invalida("O arquivo está vazio.");

        if (tamanhoBytes > tamanhoMaximoBytes)
            return Resultado<AnexoGuardado>.Invalida(
                $"O arquivo tem {tamanhoBytes / 1024 / 1024} MB e o limite é {tamanhoMaximoBytes / 1024 / 1024} MB.");

        if (!CaminhoDeAnexo.TentarDeUpload(nomeOriginal, out var pretendido, out var erro) || pretendido is null)
            return Resultado<AnexoGuardado>.Invalida(erro ?? "Nome de arquivo inválido.");

        // Procurar nome livre aqui é uma verificação-e-depois-uso: dois uploads simultâneos do mesmo nome
        // ainda podem escolher o mesmo destino. Quem FECHA essa janela é o adaptador, que recusa
        // sobrescrever — a garantia mora onde a atomicidade existe, e este laço só evita o caso comum.
        var livre = pretendido;
        for (var n = 2; await armazem.ExisteAsync(livre, ct); n++)
        {
            if (n > 9_999) return Resultado<AnexoGuardado>.Invalida($"Não consegui um nome livre para \"{pretendido.Arquivo}\".");
            livre = pretendido.ComSufixo(n);
        }

        var gravado = await armazem.GravarAsync(livre, conteudo, ct);
        log.LogInformation("Anexo {Caminho} guardado ({Bytes} bytes).", gravado, tamanhoBytes);

        return Resultado<AnexoGuardado>.Sucesso(new AnexoGuardado(gravado, MarkdownPara(gravado)));
    }

    /// <summary>
    /// O trecho que vai para dentro da nota. Imagem entra como embed; o resto entra como LINK.
    ///
    /// Um PDF de 300 páginas embutido no meio de um resumo é uma parede que empurra o texto para fora da
    /// tela — quem quer ler o PDF clica.
    /// </summary>
    public static string MarkdownPara(CaminhoDeAnexo caminho) =>
        caminho.EhImagem ? $"![[{caminho.Valor}]]" : $"[[{caminho.Valor}]]";

    public Task<CaminhoDeAnexo?> ResolverAsync(string referencia, CancellationToken ct = default) =>
        armazem.ResolverAsync(referencia, ct);

    public Task<Stream?> AbrirAsync(CaminhoDeAnexo caminho, CancellationToken ct = default) =>
        armazem.AbrirAsync(caminho, ct);
}
