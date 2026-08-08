// O DISPLAY do cronômetro de estudo, no NAVEGADOR.
//
// POR QUE NÃO EM C#: fazer o mm:ss avançar pelo circuito seria uma mensagem por segundo, por hora de
// estudo — o mesmo motivo que tirou o zoom do grafo do servidor. O tempo AUTORITATIVO (o que vira
// registro) mora no serviço C# (EstadoDoCronometro), que conta pelo relógio; aqui só se PINTA o número,
// avançando a partir do que o C# já contou. Uma diferença de meio segundo entre o que pisca e o que é
// gravado não muda decisão nenhuma.

let alvo = null
let base = 0          // segundos que o C# já havia contado quando começou a pintar
let t0 = 0            // instante (performance.now) em que esta pintura começou
let rodando = false
let timer = 0

function fmt(s) {
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const r = s % 60
  const mm = String(m).padStart(2, '0')
  const rr = String(r).padStart(2, '0')
  return h > 0 ? `${h}:${mm}:${rr}` : `${m}:${rr}`
}

function pintar(s) {
  const el = document.getElementById(alvo)
  if (el) el.textContent = fmt(s)
}

function tick() {
  const s = base + (rodando ? (performance.now() - t0) / 1000 : 0)
  pintar(Math.floor(s))
}

/// Começa a avançar o display a partir de `segundosIniciais` (o que o C# já contou).
export function iniciar(id, segundosIniciais) {
  alvo = id
  base = segundosIniciais || 0
  t0 = performance.now()
  rodando = true
  clearInterval(timer)
  tick()
  timer = setInterval(tick, 250)
}

/// Congela o display num valor (cronômetro pausado) — sem avançar.
export function congelar(id, segundos) {
  alvo = id
  rodando = false
  clearInterval(timer)
  pintar(Math.floor(segundos || 0))
}

export function parar() {
  rodando = false
  clearInterval(timer)
}
