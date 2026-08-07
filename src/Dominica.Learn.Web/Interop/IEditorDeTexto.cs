using Microsoft.JSInterop;

namespace Dominica.Learn.Web.Interop;

/// <summary>
/// A PORTA DO EDITOR. Toda conversa com o CodeMirror passa por aqui.
///
/// A especificação pede "evitar JavaScript" e "usar CodeMirror" — que são coisas contraditórias, porque
/// o CodeMirror É JavaScript. O que dá para garantir, e é o que esta interface garante, é que o JS fica
/// CONFINADO: um módulo .js, uma interface C#, e nenhum componente Razor chamando IJSRuntime direto.
///
/// O ganho é concreto e aparece no dia em que trocar CodeMirror por Monaco (ou o contrário): muda-se o
/// módulo e esta classe, e nenhuma página. Sem a porta, cada componente que tocasse no editor viraria um
/// ponto de acoplamento com uma biblioteca de terceiros — e seriam dezenas deles em cinco anos.
/// </summary>
public interface IEditorDeTexto : IAsyncDisposable
{
    /// <summary>Cria o editor no elemento indicado e devolve o controle dele.</summary>
    Task<SessaoDeEdicao> AbrirAsync(string idDoElemento, string conteudo, DotNetObjectReference<object> ouvinte);
}

/// <summary>Um editor vivo na tela. Some quando a nota é fechada.</summary>
public sealed class SessaoDeEdicao(IJSObjectReference modulo, string idDoElemento) : IAsyncDisposable
{
    public Task<string> LerAsync() => modulo.InvokeAsync<string>("ler", idDoElemento).AsTask();
    public Task EscreverAsync(string conteudo) => modulo.InvokeVoidAsync("escrever", idDoElemento, conteudo).AsTask();
    public Task FocarAsync() => modulo.InvokeVoidAsync("focar", idDoElemento).AsTask();

    /// <summary>Insere no cursor — é onde quem anexou um arquivo espera que ele apareça.</summary>
    public Task InserirAsync(string texto) => modulo.InvokeVoidAsync("inserir", idDoElemento, texto).AsTask();

    /// <summary>
    /// Insere em LINHA PRÓPRIA. Para o que só é o que é estando sozinho na linha — um cartão colado no
    /// fim de uma frase deixa de ser cartão, porque o "::" passa a dividir o texto da pessoa.
    /// </summary>
    public Task InserirBlocoAsync(string texto) => modulo.InvokeVoidAsync("inserirBloco", idDoElemento, texto).AsTask();

    public async ValueTask DisposeAsync()
    {
        // O editor vive no navegador; se ninguém o destruir, cada troca de nota deixa um CodeMirror órfão
        // segurando memória. Numa sessão de estudo de horas, isso é o navegador engasgando.
        try { await modulo.InvokeVoidAsync("destruir", idDoElemento); }
        catch (JSDisconnectedException) { /* o circuito já caiu: não há navegador para limpar */ }
    }
}

/// <summary>
/// A PORTA DA LEITURA: completa no navegador o que só o navegador desenha — fórmulas, diagramas e realce.
///
/// Separada do editor de propósito: escrever e ler são momentos diferentes, e a maioria das aberturas de
/// nota é leitura. Uma porta só obrigaria a carregar o CodeMirror para quem só quer consultar.
/// </summary>
public interface IRenderizadorDoCliente : IAsyncDisposable
{
    /// <summary>
    /// <paramref name="temFormulas"/> e <paramref name="temDiagramas"/> vêm do SERVIDOR, que analisou o
    /// Markdown. Deixar o cliente descobrir varrendo o DOM significaria carregar 4 MB de biblioteca antes
    /// de saber se são necessários.
    /// </summary>
    Task CompletarAsync(string idDoElemento, bool temFormulas, bool temDiagramas);
}

public sealed class RenderizadorDoCliente(IJSRuntime js) : IRenderizadorDoCliente
{
    private IJSObjectReference? _modulo;

    public async Task CompletarAsync(string idDoElemento, bool temFormulas, bool temDiagramas)
    {
        try
        {
            _modulo ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/leitura.js");
            await _modulo.InvokeVoidAsync("completar", idDoElemento, temFormulas, temDiagramas);
        }
        catch (JSDisconnectedException) { /* o circuito caiu no meio: não há navegador para desenhar */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_modulo is null) return;
        try { await _modulo.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}

/// <summary>
/// Implementação sobre o módulo JS. É a ÚNICA classe do projeto Web que conhece <see cref="IJSRuntime"/>
/// para fins de editor — se aparecer outra, a porta furou.
/// </summary>
public sealed class EditorCodeMirror(IJSRuntime js) : IEditorDeTexto
{
    private IJSObjectReference? _modulo;

    public async Task<SessaoDeEdicao> AbrirAsync(string idDoElemento, string conteudo, DotNetObjectReference<object> ouvinte)
    {
        // Import por módulo ES, e não script global: o navegador só baixa o editor quando alguém abre uma
        // nota. Quem entra para procurar algo e não edita nada não paga por ele.
        _modulo ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/editor.js");
        await _modulo.InvokeVoidAsync("criar", idDoElemento, conteudo, ouvinte);
        return new SessaoDeEdicao(_modulo, idDoElemento);
    }

    public async ValueTask DisposeAsync()
    {
        if (_modulo is null) return;
        try { await _modulo.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
