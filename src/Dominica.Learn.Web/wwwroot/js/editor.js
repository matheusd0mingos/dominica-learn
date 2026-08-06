// O ÚNICO JavaScript de aplicação do Dominica Learn.
//
// Ele existe porque CodeMirror é JavaScript e escrever um editor de texto próprio seria a pior decisão
// possível — a especificação diz isso, e está certa. O que se pode controlar é o TAMANHO da superfície:
// este arquivo é a superfície inteira, e conversa com o C# só pelas quatro funções exportadas abaixo.
//
// Regra para quem for mexer aqui: nenhuma REGRA DE NEGÓCIO neste arquivo. Ele move texto entre o
// navegador e o servidor. Quem decide o que fazer com o texto é o C#. No dia em que aparecer um "if" que
// fale de nota, etiqueta ou ligação, ele está no lugar errado.

const editores = new Map()

// O autosave é POR TEMPO DE SILÊNCIO, não por tecla: salvar a cada caractere entupiria o circuito
// SignalR e o disco. 1200 ms é curto o bastante para não perder trabalho e longo o bastante para que
// digitar uma frase inteira gere um salvamento só.
const SILENCIO_MS = 1200

export function criar(id, conteudo, ouvinte) {
  const alvo = document.getElementById(id)
  if (!alvo) return

  destruir(id)

  // <textarea> como base: funciona sem nenhuma dependência externa e é o degrau para o CodeMirror.
  // Trocar por CodeMirror mexe SÓ neste arquivo — é o motivo de a porta C# existir.
  const area = document.createElement('textarea')
  area.className = 'editor-nota'
  area.value = conteudo ?? ''
  area.spellcheck = true
  alvo.replaceChildren(area)

  let temporizador = null
  const avisar = () => {
    clearTimeout(temporizador)
    temporizador = setTimeout(() => {
      // O C# decide o que fazer — inclusive não fazer nada, se o conteúdo não mudou de verdade.
      ouvinte.invokeMethodAsync('AoMudarOTexto', area.value).catch(() => {})
    }, SILENCIO_MS)
  }
  area.addEventListener('input', avisar)

  // Ctrl+S força o salvamento sem esperar o silêncio: é o reflexo de quem escreve, e negá-lo faz o
  // usuário desconfiar do autosave mesmo quando ele funciona.
  area.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 's') {
      e.preventDefault()
      clearTimeout(temporizador)
      ouvinte.invokeMethodAsync('AoMudarOTexto', area.value).catch(() => {})
    }
  })

  editores.set(id, { area, temporizador: () => clearTimeout(temporizador) })
}

export function ler(id) {
  return editores.get(id)?.area.value ?? ''
}

export function escrever(id, conteudo) {
  const e = editores.get(id)
  if (!e) return
  // Preserva a posição do cursor: reescrever o valor sem isso jogaria quem está digitando para o fim do
  // texto a cada recarga vinda do servidor.
  const posicao = e.area.selectionStart
  e.area.value = conteudo ?? ''
  e.area.selectionStart = e.area.selectionEnd = Math.min(posicao, e.area.value.length)
}

export function focar(id) {
  editores.get(id)?.area.focus()
}

export function destruir(id) {
  const e = editores.get(id)
  if (!e) return
  e.temporizador()
  editores.delete(id)
}
