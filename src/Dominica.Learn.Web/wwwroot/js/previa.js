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
const cache = new Map()   // href → html; a prévia da mesma nota não é buscada duas vezes por página

let popover = null
let cronometro = 0
let ancoraAtual = null

function caminhoDaNota(a) {
  // O href pode ser relativo ("notas/X.md", das telas) ou absoluto ("/notas/X.md", do renderizador).
  // a.pathname já vem resolvido pelo navegador; o que interessa é o que vem depois de "/notas/".
  const marca = '/notas/'
  const i = a.pathname.indexOf(marca)
  if (i < 0) return null
  const resto = a.pathname.slice(i + marca.length)
  // "novo" é a tela de criar nota (link quebrado) — não há o que prever.
  if (resto === 'novo' || resto.length === 0) return null
  return resto
}

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

async function aoParar(ancora, caminho) {
  const chave = ancora.href
  if (!cache.has(chave)) {
    try {
      const resposta = await fetch('previa/' + caminho)
      if (!resposta.ok) { cache.set(chave, null); return }
      cache.set(chave, await resposta.text())
    } catch { return }   // sem rede não há prévia — e não há erro na tela por causa disso
  }
  const html = cache.get(chave)
  if (html) mostrar(ancora, html)
}

export function ligar() {
  if (!window.matchMedia('(hover: hover)').matches) return

  document.addEventListener('mouseover', (e) => {
    const ancora = e.target.closest('a[href]')
    if (!ancora || ancora === ancoraAtual) return
    const caminho = caminhoDaNota(ancora)
    if (!caminho) return

    esconder()
    ancoraAtual = ancora
    cronometro = setTimeout(() => aoParar(ancora, caminho), atraso)
  })

  document.addEventListener('mouseout', (e) => {
    if (!ancoraAtual) return
    // Saiu do link e não entrou no popover: fecha. Entrar no popover mantém — dá para rolar a prévia.
    const para = e.relatedTarget
    if (para && (ancoraAtual.contains(para) || (popover && popover.contains(para)))) return
    if (e.target === ancoraAtual || ancoraAtual.contains(e.target) || (popover && popover.contains(e.target)))
      esconder()
  })

  // Navegou (Blazor troca a página sem recarregar): o popover não pode sobreviver à tela que o abriu.
  document.addEventListener('click', esconder, true)
  window.addEventListener('scroll', esconderSoOPopover, { passive: true })
}

ligar()
