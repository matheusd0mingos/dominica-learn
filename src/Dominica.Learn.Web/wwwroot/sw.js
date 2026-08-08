// O service worker do Learn — DE PROPÓSITO, o menor possível.
//
// O QUE ELE FAZ: guarda a página offline.html e a mostra quando uma NAVEGAÇÃO falha sem rede. Só.
//
// O QUE ELE NÃO FAZ, E POR QUÊ: não cacheia o app. O template PWA do Blazor (o da documentação da
// Microsoft) cacheia tudo porque lá o app é WebAssembly e RODA no navegador. O Learn é Blazor
// Server: a tela vem do servidor por um circuito vivo, porque as notas moram no vault (arquivos no
// disco do servidor) e a revisão grava nelas. Cachear o casco aqui produziria o pior dos mundos —
// um app que ABRE offline e não funciona, com cara de quebrado em vez de cara de desconectado.
//
// Requisições que não são navegação (CSS, JS, o websocket do circuito) passam direto: este worker
// não toca nelas, e portanto não pode quebrá-las nem servi-las velhas.

const CACHE = 'learn-offline-v2'

// Os caminhos são RELATIVOS ao escopo do worker — que é o sub-caminho onde o app está servido
// (ver <base href> no App.razor). Um "/offline.html" absoluto sairia do escopo e daria 404.
const PAGINA_OFFLINE = 'offline.html'

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(CACHE).then((c) => c.add(PAGINA_OFFLINE)))
  // Ativa já: sem isto, a primeira visita registra o worker mas ele só valeria na segunda.
  self.skipWaiting()
})

self.addEventListener('activate', (e) => {
  e.waitUntil((async () => {
    // Versões antigas do cache saem — uma offline.html velha serviria um texto que já não existe.
    for (const nome of await caches.keys()) if (nome !== CACHE) await caches.delete(nome)
    await self.clients.claim()
  })())
})

self.addEventListener('fetch', (e) => {
  if (e.request.mode !== 'navigate') return   // tudo que não é navegação passa intocado

  e.respondWith(
    // REDE PRIMEIRO, E FURANDO O CACHE HTTP ('no-store'). Sem isso, o defeito visto no navegador:
    // offline, o Chromium servia o /grafo do cache de disco — uma página pré-renderizada SEM circuito,
    // meio estilizada, com cara de app quebrado em vez de app desconectado. E servir HTML velho de
    // Blazor Server é bug por si só: cada página carrega token de antiforgery daquela requisição.
    // O custo online é nenhum na prática — a página de um app Server é dinâmica e não era para ser
    // servida de cache mesmo.
    fetch(e.request, { cache: 'no-store' }).catch(() => caches.match(PAGINA_OFFLINE))
  )
})
