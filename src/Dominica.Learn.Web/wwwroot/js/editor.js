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

  const completar = ligarCompletarLigacao(cm, ouvinte)

  // O CodeMirror MEDE UMA VEZ e guarda. Ele mede na criação — quando a fonte monoespaçada pode ainda
  // não ter carregado e a coluna pode ainda não ter a largura final —, e daí em diante confia no que
  // mediu. Uma medida velha não erra só o desenho: erra o MAPA DE CLIQUE, e clicar no meio de uma
  // palavra põe o cursor noutro lugar. É o defeito que se sente como "o editor está estranho" e que
  // ninguém consegue descrever.
  //
  // Duas medidas, então: uma no quadro seguinte (a largura já é a de verdade) e outra sempre que a
  // largura mudar de fato — girar o telefone, arrastar a janela, abrir uma coluna ao lado.
  const remedir = () => cm.refresh()
  requestAnimationFrame(remedir)

  // SÓ A LARGURA. Com `height: auto` no CSS, o refresh muda a ALTURA do editor — observar altura aqui
  // faria o observador disparar a si mesmo, para sempre.
  let larguraConhecida = alvo.getBoundingClientRect().width
  const observador = new ResizeObserver(() => {
    const agora = alvo.getBoundingClientRect().width
    if (Math.abs(agora - larguraConhecida) < 1) return
    larguraConhecida = agora
    remedir()
  })
  observador.observe(alvo)

  editores.set(id, {
    cm,
    limpar: () => { clearTimeout(temporizador); completar.limpar(); observador.disconnect() },
  })
}

// ——————————————————————————————————————————————————————————————————————————————————————————
// AUTOCOMPLETAR DE [[
//
// POR QUE ISTO É O RECURSO QUE FAZ O VAULT VIRAR REDE: escrever "[[" e ter de lembrar o nome exato da
// nota é o momento em que a pessoa desiste de ligar e escreve a explicação de novo, do zero. É assim que
// um vault vira uma pilha de arquivos soltos em vez de conhecimento conectado. Três letras e a lista têm
// de aparecer.
//
// A DIVISÃO DE TRABALHO COM O C# É PROPOSITAL, e é a regra deste arquivo: o JavaScript só sabe DETECTAR
// que o cursor está dentro de um "[[" aberto e DESENHAR uma lista. Quem decide quais notas casam, em que
// ordem, e — o mais importante — se o texto inserido sai como "[[Crase]]" ou "[[Português/Crase]]" é o
// servidor. Essa última é regra de domínio: erra-se para o lado da ambiguidade, e link ambíguo não dá
// erro, leva para a nota errada em silêncio. Ver EscritaDeLigacao.
//
// UMA IDA AO SERVIDOR POR TECLA, com 90 ms de folga. Parece caro e não é: é a mesma ordem de grandeza do
// autosave que já roda aqui. A alternativa — baixar o vault inteiro e filtrar no navegador — obrigaria a
// reescrever a pontuação da busca em JavaScript, onde ela não teria teste e divergiria da do abridor
// rápido no primeiro ajuste.
const ESPERA_SUGESTAO_MS = 90
const MAX_TERMO = 60

function ligarCompletarLigacao(cm, ouvinte) {
  let caixa = null
  let sugestoes = []
  let escolhido = 0
  let pedido = null

  const fechar = () => {
    clearTimeout(pedido)
    caixa?.remove()
    caixa = null
    sugestoes = []
  }

  // O contexto: um "[[" aberto na linha do cursor, sem "]]" entre ele e o cursor.
  const contexto = () => {
    const cur = cm.getCursor()
    const linha = cm.getLine(cur.line) ?? ''
    const antes = linha.slice(0, cur.ch)

    const abre = antes.lastIndexOf('[[')
    if (abre < 0) return null

    const termo = antes.slice(abre + 2)
    // "]" no meio já fechou o link; termo comprido é quase certamente um "[[" antigo lá atrás na linha,
    // e não o que a pessoa está escrevendo agora.
    if (termo.includes(']') || termo.length > MAX_TERMO) return null

    return { termo, de: { line: cur.line, ch: abre }, ate: cur }
  }

  const desenhar = (ctx) => {
    if (!sugestoes.length) return fechar()

    if (!caixa) {
      caixa = document.createElement('div')
      caixa.className = 'cm-ligacoes'
      // No <body>, e não dentro do editor: dentro, o `overflow` do CodeMirror recortaria a lista, e o
      // sintoma seria uma lista que só mostra a primeira linha quando o cursor está perto da borda.
      document.body.appendChild(caixa)
    }

    caixa.innerHTML = ''
    sugestoes.forEach((s, i) => {
      const item = document.createElement('div')
      item.className = 'cm-ligacoes-item' + (i === escolhido ? ' cm-ligacoes-atual' : '')
      const nome = document.createElement('span')
      nome.className = 'cm-ligacoes-nome'
      nome.textContent = s.nome
      item.appendChild(nome)
      if (s.pasta) {
        const pasta = document.createElement('span')
        pasta.className = 'cm-ligacoes-pasta'
        pasta.textContent = s.pasta
        item.appendChild(pasta)
      }
      // mousedown e não click: o click chega depois do blur, e o blur já teria fechado a lista.
      item.addEventListener('mousedown', (e) => { e.preventDefault(); aceitar(i) })
      caixa.appendChild(item)
    })

    const pos = cm.cursorCoords(ctx.de, 'page')
    caixa.style.left = `${pos.left}px`
    caixa.style.top = `${pos.bottom + 4}px`

    // Se não couber abaixo, sobe. Sem isto, a lista fica fora da tela justamente quando se escreve no
    // fim da nota — que é onde se escreve quase sempre.
    const altura = caixa.offsetHeight
    if (pos.bottom - window.scrollY + altura + 16 > window.innerHeight)
      caixa.style.top = `${pos.top - altura - 4}px`
  }

  const aceitar = (i) => {
    const escolha = sugestoes[i]
    const ctx = contexto()
    if (!escolha || !ctx) return fechar()

    cm.replaceRange(`[[${escolha.insercao}]]`, ctx.de, ctx.ate)
    fechar()
    cm.focus()
  }

  const reavaliar = () => {
    const ctx = contexto()
    if (!ctx) return fechar()

    clearTimeout(pedido)
    pedido = setTimeout(() => {
      ouvinte.invokeMethodAsync('AoCompletarLigacao', ctx.termo)
        .then((r) => {
          // O cursor pode ter saído do "[[" enquanto a resposta vinha. Desenhar aqui deixaria uma lista
          // órfã flutuando sobre o texto, e ela só sumiria no próximo clique.
          const agora = contexto()
          if (!agora || agora.de.line !== ctx.de.line || agora.de.ch !== ctx.de.ch) return fechar()
          sugestoes = r ?? []
          escolhido = 0
          desenhar(agora)
        })
        .catch(() => fechar())
    }, ESPERA_SUGESTAO_MS)
  }

  cm.on('cursorActivity', reavaliar)
  cm.on('blur', fechar)

  cm.on('keydown', (_, e) => {
    if (!caixa || !sugestoes.length) return

    const mover = (d) => {
      escolhido = (escolhido + d + sugestoes.length) % sugestoes.length
      const ctx = contexto()
      if (ctx) desenhar(ctx)
    }

    switch (e.key) {
      case 'ArrowDown': mover(1); break
      case 'ArrowUp': mover(-1); break
      case 'Enter':
      case 'Tab': aceitar(escolhido); break
      case 'Escape': fechar(); break
      default: return
    }

    // codemirrorIgnore é o que impede o CodeMirror de ALÉM disso inserir uma quebra de linha no Enter.
    // Só preventDefault não basta: o CM5 trata a tecla no seu próprio despacho, antes do navegador.
    e.preventDefault()
    e.codemirrorIgnore = true
  })

  return { limpar: fechar }
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

// Insere um BLOCO — um cartão, hoje — em linha própria.
//
// Diferente de `inserir`, que é para o que entra no meio da frase (a referência a um anexo é parte do
// parágrafo em que ela aparece). Um cartão colado no fim de uma frase deixa de ser cartão: o separador
// "::" passa a dividir o texto da pessoa.
//
// O CASO DO CURSOR NA POSIÇÃO ZERO É TRATADO À PARTE, e é o que a primeira versão errou: uma nota
// recém-aberta tem o cursor no começo porque NINGUÉM O COLOCOU ALI. Inserir no cursor punha o cartão
// acima do "# Título", empurrando o cabeçalho para baixo. Quando o cursor está no começo de um
// documento que já tem texto, o lugar certo é o fim.
export function inserirBloco(id, texto) {
  const e = editores.get(id)
  if (!e) return
  const cm = e.cm

  const cursor = cm.getCursor()
  const noComeco = cursor.line === 0 && cursor.ch === 0
  const temTexto = cm.getValue().trim().length > 0

  const destino = noComeco && temTexto
    ? { line: cm.lastLine(), ch: cm.getLine(cm.lastLine()).length }
    : { line: cursor.line, ch: cm.getLine(cursor.line).length }

  // Uma linha em branco antes só quando a linha de destino tem conteúdo: senão, cada cartão criado numa
  // nota vazia deixaria um buraco no topo.
  const separador = cm.getLine(destino.line).trim().length > 0 ? '\n\n' : ''

  cm.replaceRange(separador + texto, destino)
  cm.setCursor({ line: cm.lastLine(), ch: 0 })
  cm.focus()
}

// O que está selecionado agora. É o que permite que o gesto natural — marcar a frase que importa e
// transformá-la em cartão — funcione sem redigitar nada.
export function selecao(id) {
  return editores.get(id)?.cm.getSelection() ?? ''
}

// Envolve a seleção com uma marca ("==" para lacuna). Devolve false quando não há nada selecionado, para
// que o C# possa dizer o motivo em vez de o botão simplesmente não fazer nada.
export function envolverSelecao(id, marca) {
  const e = editores.get(id)
  if (!e) return false
  const cm = e.cm
  const texto = cm.getSelection()
  if (!texto.trim()) return false

  // Já estava envolvido: DESFAZ. Sem isto, clicar duas vezes produziria "====texto====", que não é
  // lacuna nenhuma e o analisador descarta — o sintoma seria o cartão sumir da fila sem explicação.
  const jaTem = texto.startsWith(marca) && texto.endsWith(marca) && texto.length > marca.length * 2
  cm.replaceSelection(jaTem ? texto.slice(marca.length, -marca.length) : marca + texto + marca)
  cm.focus()
  return true
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
