// O HOVER PREVIEW de wikilink — pairar sobre um link de nota mostra a nota, sem navegar.
//
// É o gesto que faz a ligação valer alguma coisa DURANTE a leitura: sem ele, conferir "o que era mesmo
// aquela nota?" custa navegar, perder a posição, e voltar. Com ele, custa parar o mouse meio segundo.
//
// TUDO ACONTECE NO NAVEGADOR, sem circuito: o hover é do mouse, e acordar o Blazor a cada passada de
// ponteiro poria um round-trip na frente do popover. O conteúdo vem do endpoint /previa (fetch com URL
// RELATIVA — respeita o <base href> no sub-caminho), que responde com o MESMO HTML seguro da tela de
// leitura; o innerHTML aqui recebe o que a leitura já recebia, nada de fonte nova.
//
// SÓ ONDE HÁ HOVER DE VERDADE: no toque (celular), "pairar" não existe — o dedo que encosta é clique, e
// um popover que abre no meio do caminho do toque só atrapalha. O matchMedia decide uma vez, na carga.
//
// O QUE FICA DE FORA, de propósito: KaTeX, Mermaid e realce dentro do popover. São megabytes que se
// carregam para LER a nota; a prévia é para RECONHECÊ-LA. Fórmula aparece como texto — quem precisa
// dela clica.

const atraso = 350        // ms parado sobre o link antes de buscar — passada de mouse não é intenção

// href → { html, quando }. A prévia da mesma nota não é buscada duas vezes por página — mas o cache
// VENCE: quem edita a nota e paira de novo na mesma visita precisa ver a versão nova, não a de antes
// do F5. Um minuto cobre a rajada de hovers de uma leitura sem congelar a tarde inteira.
const cache = new Map()
const validadeMs = 60_000

let popover = null
let cronometro = 0
let ancoraAtual = null

// A URL do fetch da prévia, POR TIPO DE ALVO — e ela é também a chave do cache, para que o mesmo
// destino visto por caminhos diferentes compartilhe a prévia guardada:
//   • data-previa="Pasta/Nota.md"     → nota (nó do grafo)         → previa/<caminho>
//   • data-previa-etiqueta="#tag"     → recorte de etiqueta        → previa-etiqueta/<tag>
//   • data-previa-materia="Direito"   → recorte de matéria         → previa-materia/<materia>
//   • <a href="…/notas/Nota.md">      → nota (link das telas)      → previa/<caminho>
function urlDaPrevia(el) {
  const nota = el.getAttribute?.('data-previa')
  if (nota) return 'previa/' + nota.split('/').map(encodeURIComponent).join('/')

  const etiqueta = el.getAttribute?.('data-previa-etiqueta')
  if (etiqueta) return 'previa-etiqueta/' + encodeURIComponent(etiqueta)

  const materia = el.getAttribute?.('data-previa-materia')
  if (materia) return 'previa-materia/' + encodeURIComponent(materia)

  if (el.tagName !== 'A') return null
  const marca = '/notas/'
  const i = el.pathname.indexOf(marca)
  if (i < 0) return null
  const resto = el.pathname.slice(i + marca.length)
  // "novo" é a tela de criar nota (link quebrado) — não há o que prever.
  if (resto === 'novo' || resto.length === 0) return null
  return 'previa/' + resto
}

// Os quatro tipos de alvo que têm prévia — o seletor único que os três ouvintes usam.
const ALVOS = 'a[href], [data-previa], [data-previa-etiqueta], [data-previa-materia]'

function esconder() {
  clearTimeout(cronometro)
  ancoraAtual = null
  if (popover) popover.remove()
  popover = null
}

function mostrar(ancora, html) {
  if (ancoraAtual !== ancora) return   // o mouse já foi embora enquanto a rede respondia
  esconderSoOPopover()

  popover = document.createElement('div')
  popover.className = 'previa-de-nota'
  popover.innerHTML = html             // HTML do NOSSO renderizador (DisableHtml) — ver o topo
  document.body.appendChild(popover)

  // Perto do link, mas nunca fora da tela: abaixo se couber, senão acima; presa às bordas na horizontal.
  const r = ancora.getBoundingClientRect()
  const largura = Math.min(420, window.innerWidth - 24)
  popover.style.width = largura + 'px'
  let x = Math.max(12, Math.min(r.left, window.innerWidth - largura - 12))
  const altura = popover.offsetHeight
  let y = r.bottom + 8
  if (y + altura > window.innerHeight - 12 && r.top - altura - 8 > 12) y = r.top - altura - 8
  popover.style.left = x + 'px'
  popover.style.top = y + 'px'
}

function esconderSoOPopover() {
  if (popover) popover.remove()
  popover = null
}

async function aoParar(ancora, url) {
  const chave = url   // a URL já é única por alvo — serve de chave e de endereço
  const guardada = cache.get(chave)
  if (!guardada || Date.now() - guardada.quando > validadeMs) {
    try {
      const resposta = await fetch(url)
      if (!resposta.ok) { cache.set(chave, { html: null, quando: Date.now() }); return }
      cache.set(chave, { html: await resposta.text(), quando: Date.now() })
    } catch { return }   // sem rede não há prévia — e não há erro na tela por causa disso
  }
  const html = cache.get(chave)?.html
  if (html) mostrar(ancora, html)
}

export function ligar() {
  // O TECLADO TAMBÉM TEM PRÉVIA — e em qualquer aparelho: focar um link de nota (Tab) mostra o
  // popover, Escape fecha. :focus-visible separa o foco de teclado do foco que um clique deixa —
  // sem isso, todo clique em link abriria um popover atrás da navegação.
  document.addEventListener('focusin', (e) => {
    const ancora = e.target.closest?.(ALVOS)
    if (!ancora || !ancora.matches(':focus-visible')) return
    const url = urlDaPrevia(ancora)
    if (!url) return

    esconder()
    ancoraAtual = ancora
    cronometro = setTimeout(() => aoParar(ancora, url), 150)   // foco é intenção; espera menos
  })

  document.addEventListener('focusout', (e) => {
    if (ancoraAtual && e.target === ancoraAtual) esconder()
  })

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && popover) esconder()
  })

  // Navegou (Blazor troca a página sem recarregar): o popover não pode sobreviver à tela que o abriu.
  document.addEventListener('click', esconder, true)
  window.addEventListener('scroll', esconderSoOPopover, { passive: true })

  // O hover, só onde hover existe de verdade — no toque, o dedo que encosta é clique.
  if (!window.matchMedia('(hover: hover)').matches) return

  document.addEventListener('mouseover', (e) => {
    const ancora = e.target.closest(ALVOS)
    if (!ancora || ancora === ancoraAtual) return
    const url = urlDaPrevia(ancora)
    if (!url) return

    esconder()
    ancoraAtual = ancora
    cronometro = setTimeout(() => aoParar(ancora, url), atraso)
  })

  document.addEventListener('mouseout', (e) => {
    if (!ancoraAtual) return
    // Saiu do link e não entrou no popover: fecha. Entrar no popover mantém — dá para rolar a prévia.
    const para = e.relatedTarget
    if (para && (ancoraAtual.contains(para) || (popover && popover.contains(para)))) return
    if (e.target === ancoraAtual || ancoraAtual.contains(e.target) || (popover && popover.contains(e.target)))
      esconder()
  })
}

ligar()
