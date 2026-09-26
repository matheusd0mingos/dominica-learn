// PARES QUE SE FECHAM SOZINHOS — a metade do "autocompletar" que ainda faltava aqui.
//
// O editor já completa DUAS coisas, e as duas são de conteúdo: "[[" oferece notas e "#" oferece
// etiquetas (ver editor.js). O que não existia é o autocompletar de SINTAXE: escrever "(" e o ")"
// aparecer, marcar uma palavra e apertar "*" para ela virar *palavra*. É o que um editor de Markdown
// qualquer faz, e a falta disso é sentida a cada parêntese — sem nunca virar um defeito que alguém
// reporte, porque a pessoa só digita o fecho e segue.
//
// O GANHO MAIOR NÃO É O PARÊNTESE, É O "[[". Digitar um colchete produz "[]", e o segundo produz
// "[[]]" — ou seja, o gesto de LIGAR uma nota passou de quatro teclas para duas, e a lista de notas
// abre no mesmo instante. O recurso que faz o vault virar rede ficou a metade do caminho mais perto.
//
// POR QUE ESCRITO À MÃO, E NÃO O ADDON `closebrackets` DO CODEMIRROR: o addon não sabe Markdown. Ele
// deixaria um "*" órfão a cada item de lista ("* |*"), fecharia o "_" no meio de nome_com_underline, e
// transformaria a terceira crase de uma cerca ``` em quatro. São três guardas curtos — dois em
// `podeAbrir` e um em `espaco` — contra mais um arquivo de terceiro para versionar. É a mesma troca já
// feita para a faixa da linha atual, em editor.js.
//
// A SEMÂNTICA, essa sim, é a do addon, e de propósito: fechar ao abrir, PULAR por cima do fecho
// quando ele já está ali, e apagar o par inteiro no Backspace de um par vazio. É o comportamento que
// todo editor tem, e o único em que "digitar o texto inteiro na mão" continua dando o resultado
// certo — quem não confia no automático não é punido por isso.

// Aberto → fechado. Curta de propósito: aspas ficaram de fora porque em texto corrido português o
// apóstrofo é letra ("d'água"), e chaves porque não são sintaxe de Markdown.
const PARES = { '[': ']', '(': ')', '`': '`', '*': '*', '_': '_' }

// Os simétricos: a mesma tecla abre e fecha, então é ela quem também PULA por cima do fecho.
const SIMETRICOS = new Set(['`', '*', '_'])

// O QUE PODE VIR DEPOIS DO CURSOR para valer autofechar: nada (fim da linha), espaço, ou um fecho.
// Sem esta regra, pôr um "(" antes de uma palavra já escrita produziria "()palavra" — o fecho no
// lugar errado, que é pior do que fecho nenhum porque tem de ser apagado.
const PODE_FECHAR_ANTES_DE = /^$|^[\s)\]}>,.;:!?]/

/**
 * Devolve o mapa de teclas para o `extraKeys` do CodeMirror.
 *
 * Recebe o CodeMirror por parâmetro em vez de ler `window`: assim este arquivo não depende da ordem
 * de carregamento de ninguém, e o teste headless (novo/ferramentas/editor/pares.mjs) o exercita com
 * uma instância própria.
 */
export function atalhosDePares(CodeMirror) {
  const mapa = {
    Backspace: (cm) => apagarPar(CodeMirror, cm),
    Space: (cm) => espaco(CodeMirror, cm),
  }

  for (const abre of Object.keys(PARES)) mapa[`'${abre}'`] = (cm) => digitar(CodeMirror, cm, abre)
  // Os fechos assimétricos ganham tecla própria só para pular por cima do que foi autofechado.
  for (const fecha of [']', ')']) mapa[`'${fecha}'`] = (cm) => pular(CodeMirror, cm, fecha)

  return mapa
}

function digitar(CodeMirror, cm, abre) {
  const fecha = PARES[abre]

  // COM TEXTO SELECIONADO, ENVOLVE — e é este o recurso que mais se usa depois de instalado: marcar a
  // palavra e apertar "*". O `'around'` mantém a seleção por FORA das marcas, então apertar "*" de
  // novo produz **negrito** sem reselecionar nada.
  if (cm.somethingSelected())
    return void cm.replaceSelections(cm.getSelections().map((t) => abre + t + fecha), 'around')

  const cur = cm.getCursor()
  const linha = cm.getLine(cur.line) ?? ''
  const antes = linha.slice(0, cur.ch)
  const depois = linha.slice(cur.ch)

  // PULAR POR CIMA (só os simétricos; os assimétricos têm tecla de fecho própria). É o que faz
  // escrever "*itálico*" inteiro na mão dar "*itálico*" e não "*itálico*|*".
  if (SIMETRICOS.has(abre) && depois[0] === fecha)
    return void cm.setCursor({ line: cur.line, ch: cur.ch + 1 })

  if (!podeAbrir(abre, antes, depois)) return CodeMirror.Pass

  cm.replaceRange(abre + fecha, cur, cur)
  cm.setCursor({ line: cur.line, ch: cur.ch + 1 })
}

/** Os guardas de Markdown. Cada um existe por um caso concreto, nomeado ao lado. (O terceiro caso — a
 *  lista "* item" — NÃO se resolve aqui; ver `espaco`, e o porquê está lá.) */
function podeAbrir(abre, antes, depois) {
  if (!PODE_FECHAR_ANTES_DE.test(depois)) return false

  // nome_com_underline: "_" colado em letra ou número é parte da palavra, não ênfase.
  if (abre === '_' && /[\p{L}\p{N}_]/u.test(antes.slice(-1))) return false

  // A TERCEIRA CRASE ABRE UMA CERCA, e cerca não tem par. Sem este guarda, ``` viraria ```` — quatro
  // crases, que não abrem bloco de código nenhum e o destaque de sintaxe some da nota inteira.
  if (abre === '`' && antes.endsWith('``')) return false

  return true
}

function pular(CodeMirror, cm, fecha) {
  if (cm.somethingSelected()) return CodeMirror.Pass
  const cur = cm.getCursor()
  if ((cm.getLine(cur.line) ?? '')[cur.ch] !== fecha) return CodeMirror.Pass
  cm.setCursor({ line: cur.line, ch: cur.ch + 1 })
}

/**
 * O ÚNICO CONFLITO REAL DO "*" É A LISTA "* item" — E ELE SE RESOLVE AQUI, NO ESPAÇO.
 *
 * A primeira versão tentava resolver no asterisco: não autofechar "*" quando a linha ainda estava
 * vazia. Parecia certo e estava errado, e o teste ao lado foi quem disse — "*itálico*" começando a
 * linha (que é comum) saía "*itálico**", porque o asterisco de abertura tinha ficado sem par e o de
 * fechamento abria um novo. Consertar num lugar quebrava o outro, porque no instante do "*" as duas
 * intenções são idênticas.
 *
 * No ESPAÇO elas deixam de ser: "* " só pode ser lista. Então o "*" abre par sempre, e é o espaço
 * digitado logo em seguida, dentro de um par ainda vazio no começo da linha, que recolhe o fecho.
 */
function espaco(CodeMirror, cm) {
  if (cm.somethingSelected() || cm.listSelections().length > 1) return CodeMirror.Pass

  const cur = cm.getCursor()
  const linha = cm.getLine(cur.line) ?? ''
  // exatamente "*|*" (com indentação à esquerda, se houver): o par vazio que acabou de nascer
  if (linha.slice(0, cur.ch).trim() !== '*' || linha[cur.ch] !== '*') return CodeMirror.Pass

  cm.replaceRange(' ', cur, { line: cur.line, ch: cur.ch + 1 })
  cm.setCursor({ line: cur.line, ch: cur.ch + 1 })
}

/** Backspace num par VAZIO apaga os dois lados. Num par com conteúdo, apaga só o caractere. */
function apagarPar(CodeMirror, cm) {
  // Multicursor: deixa o Backspace normal cuidar. Apagar dois caracteres por cursor aqui exigiria
  // tratar cada seleção à parte, e o ganho não paga o risco de apagar texto que não era par.
  if (cm.somethingSelected() || cm.listSelections().length > 1) return CodeMirror.Pass

  const cur = cm.getCursor()
  const linha = cm.getLine(cur.line) ?? ''
  const abre = linha[cur.ch - 1]
  if (!abre || PARES[abre] !== linha[cur.ch]) return CodeMirror.Pass

  cm.replaceRange('', { line: cur.line, ch: cur.ch - 1 }, { line: cur.line, ch: cur.ch + 1 })
}
