using System.ComponentModel.DataAnnotations;

namespace Dominica.Learn.Infrastructure.Vault;

/// <summary>
/// Onde o vault mora e o que ignorar dentro dele. Vem de appsettings/variáveis de ambiente — nada de
/// caminho fixo no código, que é a diferença entre subir num VPS e ter de recompilar para mudar de pasta.
/// </summary>
public sealed class OpcoesDoVault
{
    public const string Secao = "Vault";

    /// <summary>Raiz do vault no disco. Em contêiner, aponte para o volume montado.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Raiz { get; set; } = string.Empty;

    /// <summary>
    /// Pastas ignoradas na varredura.
    ///
    /// ".obsidian" é a mais importante: é onde o Obsidian Desktop guarda configuração, plugins e workspace
    /// do usuário. Indexar aquilo encheria a busca de JSON de configuração — e, pior, o vigia de arquivos
    /// dispararia reconciliação a cada tecla digitada lá dentro, porque o Obsidian salva o workspace o
    /// tempo todo. ".trash" é a lixeira dele; ".git" aparece em quem versiona o vault, que é comum.
    /// </summary>
    public string[] PastasIgnoradas { get; set; } = [".obsidian", ".trash", ".git", "node_modules"];

    /// <summary>Espera antes de reconciliar depois de uma mudança no disco (ms).</summary>
    /// <remarks>
    /// O sistema de arquivos entrega eventos aos borbotões: salvar um arquivo pode gerar três ou quatro,
    /// e um `git checkout` gera milhares em segundos. Sem o silêncio, cada um viraria uma reconciliação.
    /// </remarks>
    public int EsperaDoVigiaMs { get; set; } = 750;

    /// <summary>Reconcilia tudo ao subir. Desligue só se a inicialização estiver custando caro demais.</summary>
    public bool ReconciliarAoIniciar { get; set; } = true;

    /// <summary>Tamanho máximo de uma nota (bytes). Barreira contra colar um arquivo inteiro por engano.</summary>
    public long TamanhoMaximoDaNotaBytes { get; set; } = 5 * 1024 * 1024;
}
