using Microsoft.JSInterop;

namespace Dominica.Learn.Web.Interop;

/// <summary>
/// O quadro de escrita à mão, atrás de uma porta — como o editor de texto.
///
/// A PORTA EXISTE PELA MESMA RAZÃO DE SEMPRE: nenhuma tela sabe que existe um canvas, um
/// <c>pointerdown</c> ou um <c>toDataURL</c>. Ela pede um quadro, e no fim pede um PNG.
///
/// E ELA É ESTREITA DE PROPÓSITO — abrir, desfazer, limpar, pegar o PNG. Não há "pontos", não há
/// "traços", não há pressão neste contrato: tudo isso vive e morre no navegador, porque cada ponto que
/// atravessasse esta fronteira seria uma ida e volta no circuito do Blazor Server, e uma caneta produz
/// mais de cem por segundo. Ver js/tinta.js.
/// </summary>
public interface IQuadroDeTinta : IAsyncDisposable
{
    /// <summary>Prepara o canvas para receber traço. O id é o do elemento na página.</summary>
    Task<bool> AbrirAsync(string idDoCanvas, string corDoTraco);

    Task TrocarCorAsync(string idDoCanvas, string cor);

    /// <summary>Apaga o último traço. Falso quando não havia nada a desfazer.</summary>
    Task<bool> DesfazerAsync(string idDoCanvas);

    Task LimparAsync(string idDoCanvas);

    /// <summary>Nada foi desenhado ainda — a tela usa isto para não gravar um PNG em branco.</summary>
    Task<bool> VazioAsync(string idDoCanvas);

    /// <summary>O desenho em PNG, base64 puro (sem o prefixo "data:").</summary>
    Task<string?> ParaPngBase64Async(string idDoCanvas);

    Task DestruirAsync(string idDoCanvas);
}

/// <summary>Adaptador sobre js/tinta.js. Carrega o módulo na primeira vez que alguém abre um quadro.</summary>
public sealed class QuadroDeTintaJs(IJSRuntime js) : IQuadroDeTinta
{
    private IJSObjectReference? _modulo;

    // SEM BARRA INICIAL: servido em /private/dominica-learn, "/js/..." resolveria contra a raiz do
    // domínio e daria 404. Mesmo motivo detalhado em EditorCodeMirror.
    private async Task<IJSObjectReference> ModuloAsync() =>
        _modulo ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/tinta.js");

    public async Task<bool> AbrirAsync(string idDoCanvas, string corDoTraco) =>
        await (await ModuloAsync()).InvokeAsync<bool>("abrir", idDoCanvas, corDoTraco);

    public async Task TrocarCorAsync(string idDoCanvas, string cor) =>
        await (await ModuloAsync()).InvokeVoidAsync("trocarCor", idDoCanvas, cor);

    public async Task<bool> DesfazerAsync(string idDoCanvas) =>
        await (await ModuloAsync()).InvokeAsync<bool>("desfazer", idDoCanvas);

    public async Task LimparAsync(string idDoCanvas) =>
        await (await ModuloAsync()).InvokeVoidAsync("limpar", idDoCanvas);

    public async Task<bool> VazioAsync(string idDoCanvas) =>
        await (await ModuloAsync()).InvokeAsync<bool>("vazio", idDoCanvas);

    public async Task<string?> ParaPngBase64Async(string idDoCanvas) =>
        await (await ModuloAsync()).InvokeAsync<string?>("paraPngBase64", idDoCanvas);

    public async Task DestruirAsync(string idDoCanvas)
    {
        if (_modulo is null) return;
        try { await _modulo.InvokeVoidAsync("destruir", idDoCanvas); }
        // O circuito pode já ter caído quando a tela é descartada — descartar não é hora de explodir.
        catch (JSDisconnectedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_modulo is null) return;
        try { await _modulo.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
