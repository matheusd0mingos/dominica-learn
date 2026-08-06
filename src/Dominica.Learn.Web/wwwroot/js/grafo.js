// Zoom e arrasto do grafo, no NAVEGADOR.
//
// POR QUE NÃO EM C#: cada passo da roda e cada pixel de arrasto viraria uma mensagem no circuito
// SignalR e uma re-renderização do servidor. Arrastar um grafo assim é mover um ponto, esperar, e ver
// ele chegar meio segundo depois — a interação fica inutilizável justamente onde ela precisa ser
// contínua. Mexer no viewBox é a única coisa que muda, e ela é local.
//
// O QUE ISTO **NÃO** FAZ, de propósito: recalcular o layout. As posições vêm prontas do domínio e são
// determinísticas — o mesmo vault desenha igual sempre, que é o que permite decorar onde as coisas
// ficam. Zoom muda quanto se vê, nunca onde as coisas estão.

const estados = new Map()

const MIN = 0.2   // além disso o vault vira uma nuvem de poeira
const MAX = 8

/// Liga a roda e o arrasto no <svg>. `viewBoxInicial` é o enquadramento calculado no servidor.
export function ligar(id, viewBoxInicial) {
  const svg = document.getElementById(id)
  if (!svg) return

  desligar(id)   // trocar de matéria recria o SVG; sem isto os ouvintes se acumulariam

  const [x, y, w, h] = viewBoxInicial.split(' ').map(Number)
  const inicial = { x, y, w, h }
  const estado = { svg, inicial, atual: { ...inicial }, ouvintes: [] }
  estados.set(id, estado)

  const aplicar = () => {
    const v = estado.atual
    svg.setAttribute('viewBox', `${v.x} ${v.y} ${v.w} ${v.h}`)
  }

  // —— roda: aproxima do PONTEIRO, não do centro ————————————————————————————————————————
  // Zoom para o centro obriga a pessoa a arrastar depois de cada passo, porque o que ela queria ver
  // saiu de baixo do cursor. Ancorar no ponteiro é o que faz o gesto parecer natural.
  const naRoda = (e) => {
    e.preventDefault()
    const v = estado.atual
    const r = svg.getBoundingClientRect()
    const px = (e.clientX - r.left) / r.width
    const py = (e.clientY - r.top) / r.height

    const fator = e.deltaY < 0 ? 0.85 : 1 / 0.85
    const escalaAtual = inicial.w / v.w
    const escalaNova = Math.min(MAX, Math.max(MIN, escalaAtual / fator))
    const nw = inicial.w / escalaNova
    const nh = inicial.h / escalaNova

    // o ponto sob o cursor fica onde estava
    v.x += (v.w - nw) * px
    v.y += (v.h - nh) * py
    v.w = nw
    v.h = nh
    aplicar()
  }

  // —— arrasto ——————————————————————————————————————————————————————————————————————————
  let arrastando = false
  let ultimoX = 0, ultimoY = 0

  const aoPressionar = (e) => {
    if (e.button !== 0) return
    arrastando = true
    ultimoX = e.clientX
    ultimoY = e.clientY
    svg.style.cursor = 'grabbing'
    svg.setPointerCapture?.(e.pointerId)
  }

  const aoMover = (e) => {
    if (!arrastando) return
    const v = estado.atual
    const r = svg.getBoundingClientRect()
    // converte pixels de tela para unidades do desenho: sem isso, o arrasto fica lento com zoom
    // aproximado e rápido com zoom afastado, e a mão não acompanha.
    v.x -= (e.clientX - ultimoX) * (v.w / r.width)
    v.y -= (e.clientY - ultimoY) * (v.h / r.height)
    ultimoX = e.clientX
    ultimoY = e.clientY
    aplicar()
  }

  const aoSoltar = (e) => {
    if (!arrastando) return
    arrastando = false
    svg.style.cursor = 'grab'
    svg.releasePointerCapture?.(e.pointerId)
  }

  svg.style.cursor = 'grab'
  // passive:false porque `preventDefault` no wheel é o que impede a PÁGINA de rolar junto — sem isso,
  // aproximar o grafo desce a tela e o gesto vira briga.
  const reg = [
    ['wheel', naRoda, { passive: false }],
    ['pointerdown', aoPressionar, undefined],
    ['pointermove', aoMover, undefined],
    ['pointerup', aoSoltar, undefined],
    ['pointercancel', aoSoltar, undefined],
    ['pointerleave', aoSoltar, undefined],
  ]
  for (const [nome, fn, op] of reg) { svg.addEventListener(nome, fn, op); estado.ouvintes.push([nome, fn, op]) }

  aplicar()
}

/// Volta ao enquadramento que o servidor calculou. É a saída de quem se perdeu no zoom.
export function enquadrar(id) {
  const estado = estados.get(id)
  if (!estado) return
  estado.atual = { ...estado.inicial }
  estado.svg.setAttribute('viewBox',
    `${estado.atual.x} ${estado.atual.y} ${estado.atual.w} ${estado.atual.h}`)
}

/// Aproxima ou afasta pelo centro — é o que os botões usam, para quem não tem roda (ou toque).
export function passo(id, aproximar) {
  const estado = estados.get(id)
  if (!estado) return
  const v = estado.atual
  const fator = aproximar ? 0.8 : 1.25
  const escalaNova = Math.min(MAX, Math.max(MIN, (estado.inicial.w / v.w) / fator))
  const nw = estado.inicial.w / escalaNova
  const nh = estado.inicial.h / escalaNova
  v.x += (v.w - nw) / 2
  v.y += (v.h - nh) / 2
  v.w = nw
  v.h = nh
  estado.svg.setAttribute('viewBox', `${v.x} ${v.y} ${v.w} ${v.h}`)
}

export function desligar(id) {
  const estado = estados.get(id)
  if (!estado) return
  for (const [nome, fn, op] of estado.ouvintes) estado.svg.removeEventListener(nome, fn, op)
  estados.delete(id)
}
