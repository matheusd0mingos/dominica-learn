// O quadro de tinta: escrever à mão com Apple Pencil, S Pen ou mouse.
//
// TUDO ACONTECE AQUI, NO NAVEGADOR, e essa é a decisão que manda em todo o resto do arquivo. Uma
// caneta entrega mais de cem pontos por segundo; mandar cada ponto pelo circuito SignalR do Blazor
// Server é impensável — este app já mediu o que 300 ms de latência fazem com uma ida e volta. Então o
// C# não vê ponto nenhum: ele abre o quadro, e no fim pede o PNG pronto. Mesma forma do editor de
// texto (ver IEditorDeTexto e js/editor.js): porta de interop, e nenhuma regra de negócio deste lado.
//
// SEM BIBLIOTECA. Traço com pressão é interpolar espessura entre dois pontos — são as vinte linhas de
// `desenharSegmento`. Uma dependência aqui seria mais um `dist/` para servir, mais uma CSP para ajustar
// e mais uma coisa para atualizar a cada release, em troca de nada que faltasse.

const quadros = new Map()

/**
 * A ESPESSURA VEM DA PRESSÃO, e é isso que faz o traço parecer escrito em vez de desenhado por
 * software. Sem pressão (mouse, ou caneta que não reporta), o navegador manda 0.5 — que cai bem no
 * meio da faixa e dá um traço uniforme, que é o certo para quem não tem caneta.
 */
const ESPESSURA_MINIMA = 1.2
const ESPESSURA_MAXIMA = 6

function espessura(pressao) {
  const p = pressao > 0 ? pressao : 0.5
  return ESPESSURA_MINIMA + p * (ESPESSURA_MAXIMA - ESPESSURA_MINIMA)
}

export function abrir(id, corDoTraco) {
  const canvas = document.getElementById(id)
  if (!canvas) return false

  destruir(id)

  // A RESOLUÇÃO É A DO DISPOSITIVO, e não a do CSS. Sem isto, num iPad (devicePixelRatio 2) o traço
  // sai com metade da resolução da tela e parece borrado — o defeito clássico de canvas, e o que faz
  // um quadro de tinta parecer amador na primeira olhada.
  const escala = window.devicePixelRatio || 1
  const caixa = canvas.getBoundingClientRect()
  canvas.width = Math.round(caixa.width * escala)
  canvas.height = Math.round(caixa.height * escala)

  const ctx = canvas.getContext('2d')
  ctx.scale(escala, escala)
  ctx.lineCap = 'round'
  ctx.lineJoin = 'round'
  ctx.strokeStyle = corDoTraco || '#0f2340'

  // O HISTÓRICO GUARDA TRAÇOS INTEIROS, e é o que permite desfazer. Guardar a imagem a cada traço
  // (getImageData) seria mais simples e comeria dezenas de MB numa página de contas: um traço são
  // algumas centenas de pontos, uma imagem é a tela toda.
  const estado = { canvas, ctx, escala, tracos: [], atual: null, cor: ctx.strokeStyle }

  const desenhando = (e) => {
    // A REJEIÇÃO DE PALMA, E É UMA LINHA. Havendo caneta em uso, dedo e mão apoiada não desenham —
    // que é a diferença entre poder escrever apoiando a mão no tablet e não poder. O toque continua
    // valendo enquanto nenhuma caneta tocou o quadro, para quem não tem caneta nenhuma.
    if (e.pointerType === 'pen') { estado.viuCaneta = true; return true }
    return !estado.viuCaneta
  }

  estado.aoDescer = (e) => {
    if (!desenhando(e)) return
    e.preventDefault()
    canvas.setPointerCapture(e.pointerId)
    estado.atual = { cor: estado.cor, pontos: [ponto(e, canvas)] }
  }

  estado.aoMover = (e) => {
    if (!estado.atual || !desenhando(e)) return
    e.preventDefault()

    // getCoalescedEvents: os pontos que o navegador juntou entre dois quadros. É o que separa um traço
    // liso de um traço em degraus — a 120 Hz da Pencil contra os 60 quadros da tela, dois terços dos
    // pontos estão aqui dentro e em nenhum outro lugar.
    const pontos = e.getCoalescedEvents ? e.getCoalescedEvents() : [e]
    for (const p of pontos) {
      const novo = ponto(p, canvas)
      desenharSegmento(ctx, estado.atual.pontos[estado.atual.pontos.length - 1], novo, estado.cor)
      estado.atual.pontos.push(novo)
    }
  }

  estado.aoSubir = (e) => {
    if (!estado.atual) return
    // Traço de um ponto só (um toque) vira um pingo: sem isto, pontuação não existe.
    if (estado.atual.pontos.length === 1) {
      const p = estado.atual.pontos[0]
      desenharSegmento(ctx, p, { ...p, x: p.x + 0.01 }, estado.cor)
    }
    estado.tracos.push(estado.atual)
    estado.atual = null
    if (canvas.hasPointerCapture?.(e.pointerId)) canvas.releasePointerCapture(e.pointerId)
  }

  canvas.addEventListener('pointerdown', estado.aoDescer)
  canvas.addEventListener('pointermove', estado.aoMover)
  canvas.addEventListener('pointerup', estado.aoSubir)
  // pointercancel acontece quando o sistema toma o ponteiro (gesto do iPad, notificação). Sem tratá-lo,
  // o traço fica "aberto" e o próximo toque continua a linha de onde ela parou, atravessando a tela.
  canvas.addEventListener('pointercancel', estado.aoSubir)
  canvas.addEventListener('pointerleave', estado.aoSubir)

  quadros.set(id, estado)
  return true
}

function ponto(e, canvas) {
  const caixa = canvas.getBoundingClientRect()
  return {
    x: e.clientX - caixa.left,
    y: e.clientY - caixa.top,
    pressao: e.pressure,
  }
}

/**
 * Um segmento com espessura variável.
 *
 * DOIS SEGMENTOS EM VEZ DE UM, com a espessura de cada ponta: traçar o segmento inteiro com uma
 * espessura só faz a linha mudar de grossura em degraus visíveis a cada ponto. Interpolar pelo meio é
 * o que dá a transição contínua que se espera de tinta.
 */
function desenharSegmento(ctx, de, para, cor) {
  const meio = { x: (de.x + para.x) / 2, y: (de.y + para.y) / 2 }
  ctx.strokeStyle = cor

  ctx.beginPath()
  ctx.lineWidth = espessura(de.pressao)
  ctx.moveTo(de.x, de.y)
  ctx.lineTo(meio.x, meio.y)
  ctx.stroke()

  ctx.beginPath()
  ctx.lineWidth = espessura(para.pressao)
  ctx.moveTo(meio.x, meio.y)
  ctx.lineTo(para.x, para.y)
  ctx.stroke()
}

export function trocarCor(id, cor) {
  const e = quadros.get(id)
  if (e) e.cor = cor
}

/** Redesenha tudo do histórico. Usado por desfazer e limpar. */
function repintar(estado) {
  const { ctx, canvas, escala } = estado
  ctx.clearRect(0, 0, canvas.width / escala, canvas.height / escala)
  for (const traco of estado.tracos)
    for (let i = 1; i < traco.pontos.length; i++)
      desenharSegmento(ctx, traco.pontos[i - 1], traco.pontos[i], traco.cor)
}

export function desfazer(id) {
  const estado = quadros.get(id)
  if (!estado || estado.tracos.length === 0) return false
  estado.tracos.pop()
  repintar(estado)
  return true
}

export function limpar(id) {
  const estado = quadros.get(id)
  if (!estado) return
  estado.tracos = []
  repintar(estado)
}

export function vazio(id) {
  const estado = quadros.get(id)
  return !estado || estado.tracos.length === 0
}

/**
 * O PNG, em base64, SEM o prefixo "data:".
 *
 * O FUNDO É PINTADO DE BRANCO ANTES, e não deixado transparente. Um PNG transparente fica lindo no
 * tema claro e some no tema escuro — traço marinho sobre fundo marinho. E ele vai para o vault, onde
 * quem o abre é o Obsidian de alguém, com o tema DELE. Fundo branco é a única escolha que funciona
 * nos dois lados e no papel.
 */
export function paraPngBase64(id) {
  const estado = quadros.get(id)
  if (!estado) return null

  const { canvas } = estado
  const saida = document.createElement('canvas')
  saida.width = canvas.width
  saida.height = canvas.height

  const ctx = saida.getContext('2d')
  ctx.fillStyle = '#ffffff'
  ctx.fillRect(0, 0, saida.width, saida.height)
  ctx.drawImage(canvas, 0, 0)

  return saida.toDataURL('image/png').split(',')[1]
}

export function destruir(id) {
  const estado = quadros.get(id)
  if (!estado) return
  const { canvas } = estado
  canvas.removeEventListener('pointerdown', estado.aoDescer)
  canvas.removeEventListener('pointermove', estado.aoMover)
  canvas.removeEventListener('pointerup', estado.aoSubir)
  canvas.removeEventListener('pointercancel', estado.aoSubir)
  canvas.removeEventListener('pointerleave', estado.aoSubir)
  quadros.delete(id)
}
