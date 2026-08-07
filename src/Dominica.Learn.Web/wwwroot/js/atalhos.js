// Atalhos de teclado globais.
//
// POR QUE NO DOCUMENTO E NÃO NUM ELEMENTO BLAZOR: o atalho tem de funcionar com o foco em qualquer
// lugar — inclusive dentro do CodeMirror, que engole os eventos de tecla dele. Um @onkeydown num
// componente só dispara quando o foco está nele, e "só funciona se você clicar fora do editor antes"
// é o mesmo que não funcionar.

let ouvinte = null

export function ligar(referencia) {
  desligar()

  ouvinte = (e) => {
    // Ctrl+K e Ctrl+O: o primeiro é o que a maioria das ferramentas usa hoje; o segundo é o do
    // Obsidian, e quem vem de lá tenta esse antes de ler documentação nenhuma.
    const combinacao = (e.ctrlKey || e.metaKey) && !e.altKey && (e.key === 'k' || e.key === 'o')
    if (!combinacao) return

    // preventDefault é obrigatório: Ctrl+O abre o seletor de ARQUIVOS do navegador e Ctrl+K vai para
    // a barra de endereços em alguns. Sem isto, o atalho faria as duas coisas ao mesmo tempo.
    e.preventDefault()
    // O foco é dado AQUI, depois que o .NET terminou de abrir o diálogo — e não por uma chamada de
    // volta do C#, que dependeria de o render já ter acontecido. `await` no invoke garante a ordem.
    referencia.invokeMethodAsync('AbrirBuscaAsync').then(focarBusca)
  }

  // `capture: true` para chegar antes do CodeMirror, que registra os ouvintes dele no elemento e
  // pararia a propagação antes de o documento ver a tecla.
  document.addEventListener('keydown', ouvinte, { capture: true })
}

export function desligar() {
  if (!ouvinte) return
  document.removeEventListener('keydown', ouvinte, { capture: true })
  ouvinte = null
}

/// Põe o cursor no campo do abridor rápido.
///
/// POR QUE NÃO O `FocusAsync` DO MUDBLAZOR: medido, ele não pega neste diálogo — o activeElement
/// continuava sendo "DIV.fixed pointer-events-none", a sobreposição do próprio MudBlazor, e digitar
/// não ia a lugar nenhum. `AutoFocus` no MudTextField também não resolve quando o diálogo é aberto
/// por código. Achar o input no DOM é o caminho que não depende de nenhuma dessas suposições.
export function focarBusca() {
  // INSISTE, e a insistência é o remédio para um problema específico: o diálogo do MudBlazor tem
  // armadilha de foco própria, que puxa o foco para a sobreposição DEPOIS que o conteúdo entra no
  // DOM. Uma única tentativa — mesmo já com o campo na tela — era desfeita logo em seguida; medido,
  // o activeElement voltava a ser "DIV.fixed pointer-events-none".
  //
  // Então tentamos algumas vezes ao longo de ~400 ms e paramos assim que o campo REALMENTE ficou com
  // o foco. É contorno, não elegância: o certo seria o MudBlazor aceitar um elemento inicial de foco.
  let tentativas = 0
  const tentar = () => {
    const campo = document.querySelector('.mud-dialog input[type="text"]')
    if (campo) {
      campo.focus()
      campo.select()
      if (document.activeElement === campo) return
    }
    if (++tentativas < 20) setTimeout(tentar, 20)
  }
  tentar()
}
