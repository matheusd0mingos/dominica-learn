// Editor de notas sobre CodeMirror 5.
//
// POR QUE CODEMIRROR 5, E NÃO 6: o 6 é distribuído só em módulos npm e exige um empacotador. Trazer um
// passo de build Node para um projeto .NET, num produto que vai ser mantido por anos por uma pessoa, é
// uma engrenagem a mais para quebrar — e ela quebra sempre na hora de fazer deploy. O 5 é um `dist` que
// funciona servido de uma pasta, é estável há anos, e faz exatamente o que um editor de Markdown precisa.
//
// AS BIBLIOTECAS SÃO SERVIDAS DAQUI, não de CDN. Não é preferência: a CSP em CabecalhosDeSeguranca só
// permite 'self', e CDN externo seria uma dependência de runtime — o dia em que ele cair ou mudar a URL,
// o editor para de abrir. Vault de estudo não pode depender da infraestrutura de terceiro para escrever.
//
// Regra para quem mexer aqui: nenhuma REGRA DE NEGÓCIO neste arquivo. Ele move texto entre o navegador e
// o servidor. Quem decide o que fazer com o texto é o C#.

const editores = new Map()

// O autosave é POR TEMPO DE SILÊNCIO, não por tecla: salvar a cada caractere entupiria o circuito
// SignalR e o disco. 1200 ms é curto para não perder trabalho e longo para que digitar uma frase inteira
// gere um salvamento só.
const SILENCIO_MS = 1200

let carregado = null
function carregarCodeMirror() {
  // Carrega uma vez e só quando alguém abre uma nota. Quem entra para procurar algo e não edita nada não
  // paga o download do editor.
  if (carregado) return carregado
  carregado = (async () => {
    // SEM BARRA INICIAL. `/lib/...` é relativo à RAIZ DO DOMÍNIO e ignora o <base href>: servido
    // em /private/dominica-learn, o navegador pediria dominio.com/lib/... e levaria 404. Sem a
    // barra, o caminho resolve contra o <base> e acerta nas duas formas de hospedagem.
    //
    // Aqui isso é mais traiçoeiro que num link comum: um <link> que dá 404 não reclama, e o
    // sintoma seria "o editor abriu sem estilo nenhum" — só em PRODUÇÃO, porque em
    // desenvolvimento o app também atende na raiz e a barra funciona por acidente. Foi assim
    // que estava. Ver OpcoesDeHospedagem e learn/docs/DEPLOY.md.
    await css('lib/codemirror/codemirror.css')
    await script('lib/codemirror/codemirror.js')
    // xml vem antes do markdown: o modo markdown o usa para destacar HTML embutido
    await script('lib/codemirror/xml.js')
    await script('lib/codemirror/markdown.js')
    await script('lib/codemirror/continuelist.js')
  })()
  return carregado
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
  return new Promise((ok) => {
    if (document.querySelector(`link[href="${href}"]`)) return ok()
    const l = document.createElement('link')
    l.rel = 'stylesheet'
    l.href = href
    l.onload = ok
    l.onerror = ok   // sem estilo o editor ainda funciona; sem script, não
    document.head.appendChild(l)
  })
}

export async function criar(id, conteudo, ouvinte) {
  const alvo = document.getElementById(id)
  if (!alvo) return

  destruir(id)
  await carregarCodeMirror()

  const cm = window.CodeMirror(alvo, {
    value: conteudo ?? '',
    mode: 'markdown',
    lineNumbers: false,
    lineWrapping: true,
    // Quebra visual respeitando a indentação: numa lista aninhada de estudo, a linha que vaza fica
    // alinhada com o texto do item, não colada na margem.
    indentUnit: 2,
    tabSize: 2,
    // Enter dentro de uma lista continua a lista. É o comportamento que quem escreve resumo espera, e a
    // ausência dele é a primeira coisa que faz um editor parecer amador.
    extraKeys: {
      Enter: 'newlineAndIndentContinueMarkdownList',
      'Ctrl-S': (editor) => enviar(editor),
      'Cmd-S': (editor) => enviar(editor),
    },
  })

  let temporizador = null
  const enviar = (editor) => {
    clearTimeout(temporizador)
    // O C# decide o que fazer — inclusive não fazer nada, se o conteúdo não mudou de verdade.
    ouvinte.invokeMethodAsync('AoMudarOTexto', editor.getValue()).catch(() => {})
  }

  cm.on('change', () => {
    clearTimeout(temporizador)
    temporizador = setTimeout(() => enviar(cm), SILENCIO_MS)
  })

  editores.set(id, { cm, limpar: () => clearTimeout(temporizador) })
  cm.refresh()
}

export function ler(id) {
  return editores.get(id)?.cm.getValue() ?? ''
}

export function escrever(id, conteudo) {
  const e = editores.get(id)
  if (!e) return
  // Preserva a posição do cursor: reescrever o valor sem isso jogaria quem está digitando para o fim do
  // texto a cada recarga vinda do servidor.
  const cursor = e.cm.getCursor()
  e.cm.setValue(conteudo ?? '')
  e.cm.setCursor(cursor)
}

// Insere no CURSOR, e não no fim do texto: quem anexa uma imagem está escrevendo o parágrafo em que ela
// entra. Jogar o `![[…]]` para o fim da nota obrigaria a recortar e colar toda vez.
export function inserir(id, texto) {
  const e = editores.get(id)
  if (!e) return
  e.cm.replaceSelection(texto)
  e.cm.focus()
}

export function focar(id) {
  editores.get(id)?.cm.focus()
}

export function destruir(id) {
  const e = editores.get(id)
  if (!e) return
  e.limpar()
  // O editor vive no DOM do navegador; sem limpar, cada troca de nota deixa um CodeMirror órfão segurando
  // memória. Numa sessão de estudo de horas, isso é o navegador engasgando.
  e.cm.getWrapperElement()?.remove()
  editores.delete(id)
}
