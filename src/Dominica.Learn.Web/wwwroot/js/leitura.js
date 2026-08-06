// Completa a renderização da nota NO CLIENTE: fórmulas (KaTeX), diagramas (Mermaid) e realce de código.
//
// O servidor já entregou HTML seguro — o Markdig escapou todo HTML bruto. O que sobra aqui é o que só o
// navegador sabe fazer: desenhar. Nenhuma destas bibliotecas recebe HTML para interpretar; todas leem
// TEXTO (textContent) e produzem o desenho delas.
//
// CARREGAMENTO SOB DEMANDA, e isto é metade da promessa de "carregamento instantâneo": o Mermaid tem
// 2,5 MB e o KaTeX 1,5 MB. Uma nota de texto puro — a maioria — não baixa nenhum dos dois. O C# diz na
// chamada se a nota TEM diagrama ou fórmula, porque quem sabe isso é quem analisou o Markdown.

const carregados = new Map()

function umaVez(chave, carregar) {
  if (!carregados.has(chave)) carregados.set(chave, carregar())
  return carregados.get(chave)
}

function script(src) {
  return new Promise((ok, erro) => {
    if (document.querySelector(`script[src="${src}"]`)) return ok()
    const s = document.createElement('script')
    s.src = src
    s.onload = ok
    s.onerror = () => erro(new Error(`falhou ao carregar ${src}`))
    document.head.appendChild(s)
  })
}

function css(href) {
  if (document.querySelector(`link[href="${href}"]`)) return
  const l = document.createElement('link')
  l.rel = 'stylesheet'
  l.href = href
  document.head.appendChild(l)
}

/**
 * @param {string} id        elemento que contém o HTML renderizado
 * @param {boolean} formulas a nota tem $…$ — o C# já sabe, não vale varrer o DOM para descobrir
 * @param {boolean} diagramas a nota tem bloco ```mermaid
 */
export async function completar(id, formulas, diagramas) {
  const alvo = document.getElementById(id)
  if (!alvo) return

  // Realce de código é barato (128 KB) e quase toda nota técnica tem um bloco — carrega sempre que
  // houver <pre><code>, sem perguntar.
  if (alvo.querySelector('pre code')) {
    await umaVez('hl', async () => {
      css('/lib/highlight/github.min.css')
      await script('/lib/highlight/highlight.min.js')
    })
    alvo.querySelectorAll('pre code').forEach((bloco) => {
      // O Mermaid usa <pre class="mermaid"> sem <code>, então não colide com isto.
      try { window.hljs.highlightElement(bloco) } catch { /* linguagem desconhecida: fica sem realce */ }
    })
  }

  if (formulas) {
    await umaVez('katex', async () => {
      css('/lib/katex/katex.min.css')
      await script('/lib/katex/katex.min.js')
      await script('/lib/katex/auto-render.min.js')
    })
    try {
      window.renderMathInElement(alvo, {
        // OS DELIMITADORES SÃO OS DO SERVIDOR, não os do arquivo .md.
        //
        // O usuário escreve "$E=mc^2$", mas a extensão de matemática do Markdig já converte isso para
        // "\(E=mc^2\)" antes do HTML chegar aqui — a convenção LaTeX. Configurar só "$…$" faz o KaTeX
        // não encontrar nada e a fórmula aparecer na tela como "\(E = mc^2\)", código cru. Os quatro
        // ficam listados porque uma nota pode trazer LaTeX colado de um PDF, já com \( ou \[.
        delimiters: [
          { left: '\\[', right: '\\]', display: true },
          { left: '$$', right: '$$', display: true },
          { left: '\\(', right: '\\)', display: false },
          { left: '$', right: '$', display: false },
        ],
        // Fórmula malformada não pode derrubar a leitura da nota inteira: mostra em vermelho e segue.
        throwOnError: false,
        // Não mexer no que já é código: "$PATH" num exemplo de shell não é fórmula.
        ignoredTags: ['script', 'noscript', 'style', 'textarea', 'pre', 'code'],
      })
    } catch { /* sem fórmulas desenhadas a nota ainda é legível */ }
  }

  if (diagramas) {
    await umaVez('mermaid', async () => {
      await script('/lib/mermaid/mermaid.min.js')
      window.mermaid.initialize({
        startOnLoad: false,
        // securityLevel 'strict' desliga clique e HTML dentro do diagrama. O conteúdo vem de nota que o
        // usuário colou de qualquer lugar — mesmo raciocínio do DisableHtml no servidor.
        securityLevel: 'strict',
        theme: 'neutral',
      })
    })
    try { await window.mermaid.run({ nodes: alvo.querySelectorAll('.mermaid') }) }
    catch { /* diagrama com sintaxe errada: fica o texto do bloco, que já diz o que está errado */ }
  }
}
