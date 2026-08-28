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

// Importação ESTÁTICA, e não `script(...)`: pares.js é módulo NOSSO, não biblioteca de terceiro. O
// caminho resolve contra a URL deste arquivo (não contra o <base href>), então funciona igual na raiz
// e sob /private/dominica-learn — que é exatamente a armadilha descrita em carregarCodeMirror.
import { atalhosDePares } from './pares.js'

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
    // EM PARALELO ATÉ ONDE A DEPENDÊNCIA PERMITE, e a diferença é a que se sente.
    //
    // Isto era cinco `await` em fila. Cada um é uma ida e volta na rede: em localhost, microssegundos;
    // pela internet, com 300 ms de latência, ~1,5 s SÓ para baixar os cinco — e nesse tempo a div do
    // editor fica vazia. Quem clica e digita ali não está digitando em lugar nenhum, porque o editor
    // ainda não existe. Medido: 3,5 s até o editor aparecer, com 300 ms de latência.
    //
    // As dependências são reais e continuam respeitadas: o modo markdown usa o xml para destacar HTML
    // embutido, e os dois modos precisam do codemirror.js. O que NÃO era real é a fila: o continuelist
    // não depende do xml. Três rodadas em vez de cinco.
    //
    // O CSS NÃO ESTÁ AQUI de propósito: quem declara a folha é a tela, em Notas.razor. Se o estilo
    // chegar depois de o editor nascer, o `cm.refresh()` lá embaixo remede — é para isso que ele existe.
    await script('lib/codemirror/codemirror.js')
    await script('lib/codemirror/xml.js')
    await Promise.all([
      script('lib/codemirror/markdown.js'),
      script('lib/codemirror/continuelist.js'),
    ])
  })()
  return carregado
}

// NÃO PERGUNTE AO <head> SE JÁ CARREGOU — LEMBRE. Este mapa é a memória: url → a promessa daquele
// carregamento. Pedir duas vezes devolve a mesma promessa, e ninguém baixa nada duas vezes.
//
// A versão anterior perguntava ao DOM (`document.querySelector('link[href=...]')`), e foi assim que
// nasceu o pior defeito que este editor já teve. A tela das notas declarava, no <head>, um
// <link rel="preload" as="style" href=".../codemirror.css">. Preload BAIXA, mas NÃO APLICA. O seletor
// encontrava esse preload, concluía "já está aí", e o <link rel="stylesheet"> de verdade nunca era
// acrescentado — o CodeMirror rodava sem estilo nenhum, com um bloco escuro de 50 px na direita, o
// .CodeMirror-measure visível cuspindo o "xxxxxxxxxx" que ele usa para medir a fonte, e o teclado sem
// mover o cursor. Não parecia falta de CSS. Parecia editor quebrado.
//
// Duas coisas mudaram por causa disso, e as duas importam:
//
// 1. O <head> DEIXOU DE SER O REGISTRO de quem já carregou o quê. Ele nunca foi bom nesse papel: é de
//    todo mundo, e qualquer link ou script que alguém acrescente ali amanhã, por qualquer motivo,
//    volta a poder responder por este código. Este mapa não tem esse buraco.
//
// 2. ESTE ARQUIVO NÃO CARREGA MAIS CSS. Quem declara a folha do CodeMirror é a própria tela, em
//    Notas.razor, com um <link rel="stylesheet"> comum. É menos código aqui e uma decisão a menos no
//    JavaScript — e o defeito acima passa a ser impossível, não só improvável.
const pedidos = new Map()

function script(src) {
  let p = pedidos.get(src)
  if (!p) {
    p = new Promise((ok, erro) => {
      const s = document.createElement('script')
      s.src = src
      s.onload = ok
      s.onerror = () => erro(new Error(`falhou ao carregar ${src}`))
      document.head.appendChild(s)
    })
    pedidos.set(src, p)
  }
  return p
}

// A GERAÇÃO DO EDITOR — e ela existe por causa de um defeito que só aparece com latência de rede.
//
// A div do editor tem id CONSTANTE ("editor-da-nota"): é sempre a mesma para toda nota e para toda
// visita à tela. Quando se sai de /notas e volta, o Blazor DESCARTA o componente antigo de forma
// ASSÍNCRONA e cria o novo sem esperar o descarte terminar. Em localhost isso dura microssegundos e
// nunca se cruza. Pela internet, o descarte leva centenas de milissegundos — e a ordem que sai é:
//
//     novo: destruir(id) → cria o editor → registra
//     ANTIGO (atrasado): destruir(id) → APAGA O EDITOR DO NOVO
//
// O resultado é uma div vazia: sem editor, sem textarea, sem cursor. A tela parece carregada, o texto
// que ficou desenhado é lixo do render anterior, e o teclado não vai a lugar nenhum. Reproduzido aqui
// com 300 ms de latência: seis idas e voltas, seis divs vazias.
//
// A geração conserta pela raiz: quem cria recebe um número, e DESTRUIR SÓ VALE PARA QUEM O CRIOU. Um
// descarte atrasado do componente antigo chega com um número velho e não mexe em nada.
let geracao = 0

export async function criar(id, conteudo, ouvinte) {
  const alvo = document.getElementById(id)
  if (!alvo) return 0

  const minha = ++geracao
  esvaziar(id, alvo)
  await carregarCodeMirror()

  // Alguém mais novo começou enquanto o CodeMirror carregava. Seguir criaria DOIS editores na mesma
  // div — o de baixo desenhando e o de cima comendo as teclas, que é o outro rosto do mesmo defeito.
  if (minha !== geracao) return 0

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
      // "(" fecha sozinho, "*" envolve o que estiver selecionado, e "[[" nasce inteiro — o que faz a
      // lista de notas abrir com duas teclas em vez de quatro. Ver pares.js.
      ...atalhosDePares(window.CodeMirror),
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

  // TRÊS COMPLETADORES, UM MECANISMO SÓ. Só um pode estar aberto por vez, e isso não é sorte: os
  // contextos se excluem por construção (dentro de um "[[" aberto, "#" é âncora de seção e não
  // etiqueta — ver contextoDeEtiqueta; e o de palavra se cala quando qualquer um dos outros dois
  // reconhece o lugar). Sem essa exclusão, Enter seria tratado duas vezes.
  //
  // A ORDEM IMPORTA para a leitura, não para o funcionamento: os dois primeiros são de CONTEÚDO (o
  // servidor decide o que casa), o terceiro é de DIGITAÇÃO (o próprio texto da nota é o dicionário).
  const completadores = [
    ligarCompletar(cm, completarLigacao(ouvinte)),
    ligarCompletar(cm, completarEtiqueta(ouvinte)),
    ligarCompletar(cm, completarPalavra()),
  ]
  const completar = { limpar: () => completadores.forEach((c) => c.limpar()) }

  // A LINHA ONDE O CURSOR ESTÁ, marcada.
  //
  // O cursor PISCA — metade do tempo ele não está lá para ser encontrado. Numa nota de trinta linhas,
  // isso obriga a caçar o traço a cada vez que se olha para o teclado e volta. A faixa não pisca: ela
  // responde "você está aqui" no intervalo em que o traço sumiu, e é o que faz o editor deixar de
  // parecer um campo de texto morto.
  //
  // São dez linhas em vez do addon `active-line` do CodeMirror, e a troca é consciente: o addon faria
  // exatamente isto e traria mais um arquivo de terceiro para versionar e atualizar. A pintura mora no
  // app.css, com os tokens do tema.
  let linhaMarcada = null
  const marcarLinhaAtual = () => {
    const linha = cm.getCursor().line
    if (linha === linhaMarcada) return
    if (linhaMarcada !== null) cm.removeLineClass(linhaMarcada, 'background', 'CodeMirror-activeline-background')
    // Só com o cursor SOLTO. Com texto selecionado a faixa brigaria com a seleção, pintando por baixo
    // dela uma segunda cor que não quer dizer nada.
    if (cm.somethingSelected()) { linhaMarcada = null; return }
    cm.addLineClass(linha, 'background', 'CodeMirror-activeline-background')
    linhaMarcada = linha
  }
  cm.on('cursorActivity', marcarLinhaAtual)
  // Sem foco não há cursor, e uma faixa apontando para um cursor que não existe é ruído.
  cm.on('blur', () => {
    if (linhaMarcada !== null) cm.removeLineClass(linhaMarcada, 'background', 'CodeMirror-activeline-background')
    linhaMarcada = null
  })
  cm.on('focus', marcarLinhaAtual)
  marcarLinhaAtual()

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
    geracao: minha,
    limpar: () => { clearTimeout(temporizador); completar.limpar(); observador.disconnect() },
  })

  // FOCA AQUI, e não numa chamada separada do C#: cada interop é uma ida e volta na rede, e quem abre
  // uma nota quer o cursor piscando — não em mais 300 ms.
  cm.focus()

  // O número volta para o C#, que o guarda na sessão e o devolve ao destruir. É o crachá.
  return minha
}

/// Tira da div QUALQUER editor que esteja lá — o registrado e os órfãos que uma corrida anterior possa
/// ter deixado. É a rede de segurança: mesmo que a contabilidade da geração falhe um dia, a div nunca
/// acumula dois CodeMirror.
function esvaziar(id, alvo) {
  const e = editores.get(id)
  if (e) { e.limpar(); e.cm.getWrapperElement()?.remove(); editores.delete(id) }
  alvo.querySelectorAll(':scope > .CodeMirror').forEach((n) => n.remove())
}

// ——————————————————————————————————————————————————————————————————————————————————————————
// AUTOCOMPLETAR DE [[ E DE #
//
// POR QUE O DE [[ É O RECURSO QUE FAZ O VAULT VIRAR REDE: escrever "[[" e ter de lembrar o nome exato da
// nota é o momento em que a pessoa desiste de ligar e escreve a explicação de novo, do zero. É assim que
// um vault vira uma pilha de arquivos soltos em vez de conhecimento conectado. Três letras e a lista têm
// de aparecer.
//
// E POR QUE O DE # RESOLVE OUTRO PROBLEMA, não o mesmo: ali não se trata de lembrar um nome, e sim de não
// inventar um. Três semanas depois ninguém sabe se marcou #pegadinha ou #pegadinhas; sem a lista, marca-se
// a variação nova, e cada uma passa a ter um pedaço do assunto — sem erro em lugar nenhum. Por isso a
// lista mostra QUANTAS NOTAS já usam cada etiqueta: é o número que diz qual é a de verdade.
//
// A DIVISÃO DE TRABALHO COM O C# É PROPOSITAL, e é a regra deste arquivo: o JavaScript só sabe DETECTAR
// que o cursor está num contexto e DESENHAR uma lista. Quem decide o que casa, em que ordem, e — no caso
// da ligação — se o texto sai como "[[Crase]]" ou "[[Português/Crase]]" é o servidor. Essa última é regra
// de domínio: erra-se para o lado da ambiguidade, e link ambíguo não dá erro, leva para a nota errada em
// silêncio. Ver EscritaDeLigacao e BuscaDeEtiquetas.
//
// UMA IDA AO SERVIDOR POR TECLA, com 90 ms de folga. Parece caro e não é: é a mesma ordem de grandeza do
// autosave que já roda aqui. A alternativa — baixar o vault inteiro e filtrar no navegador — obrigaria a
// reescrever a pontuação da busca em JavaScript, onde ela não teria teste e divergiria da do abridor
// rápido no primeiro ajuste.
const ESPERA_SUGESTAO_MS = 90
const MAX_TERMO = 60

/** O "[[" aberto na linha do cursor, sem "]]" entre ele e o cursor. */
function contextoDeLigacao(cm) {
  const cur = cm.getCursor()
  const linha = cm.getLine(cur.line) ?? ''
  const antes = linha.slice(0, cur.ch)

  const abre = antes.lastIndexOf('[[')
  if (abre < 0) return null

  const termo = antes.slice(abre + 2)
  // "]" no meio já fechou o link; termo comprido é quase certamente um "[[" antigo lá atrás na linha,
  // e não o que a pessoa está escrevendo agora.
  if (termo.includes(']') || termo.length > MAX_TERMO) return null

  // O "]]" QUE JÁ ESTÁ À DIREITA ENTRA NO TRECHO A SUBSTITUIR. Desde que os colchetes se fecham
  // sozinhos (pares.js), digitar "[[" produz "[[]]" — e aceitar uma sugestão escrevia o "[[Nota]]"
  // inteiro por cima só do que estava ANTES do cursor, deixando "[[Nota]]]]" na nota. Link com quatro
  // colchetes não dá erro: ele simplesmente não é link, e a nota fica órfã sem ninguém perceber.
  const depois = linha.slice(cur.ch)
  const sobra = depois.startsWith(']]') ? 2 : depois.startsWith(']') ? 1 : 0

  return { termo, de: { line: cur.line, ch: abre }, ate: { line: cur.line, ch: cur.ch + sobra } }
}

/**
 * O "#" que está virando etiqueta — e não as outras quatro coisas que "#" é em Markdown.
 *
 * As exclusões daqui são as MESMAS do analisador que lê a nota no servidor (ver AnalisadorDeNota), e é de
 * propósito: uma lista que se oferece onde o servidor não vai reconhecer etiqueta nenhuma ensina uma
 * regra que não existe. Ficam de fora:
 *
 *   "# Título"          cabeçalho — "#" na primeira coluna e um espaço logo adiante
 *   "C#", "x#y"         sem fronteira antes: "#" colado em letra não abre etiqueta
 *   "[[Nota#Seção]]"    dentro de um "[[" aberto, "#" é âncora de seção
 *   dentro de ``` ```   bloco de código; "#" ali é comentário de shell, não etiqueta
 */
function contextoDeEtiqueta(cm) {
  const cur = cm.getCursor()
  const linha = cm.getLine(cur.line) ?? ''
  const antes = linha.slice(0, cur.ch)

  const jogo = antes.lastIndexOf('#')
  if (jogo < 0) return null

  // Âncora de seção dentro de uma ligação ainda aberta.
  const abre = antes.lastIndexOf('[[')
  if (abre >= 0 && !antes.slice(abre).includes(']]')) return null

  // Fronteira à esquerda. `undefined` (começo da linha) É fronteira: uma etiqueta sozinha numa linha é
  // o caso mais comum de todos.
  const anterior = antes[jogo - 1]
  if (anterior !== undefined && !/[\s([>"',;]/.test(anterior)) return null

  const termo = antes.slice(jogo + 1)
  // Espaço encerra a etiqueta; "#" seguido de "#" é cabeçalho de nível 2 em diante.
  if (termo.length > MAX_TERMO || /[\s#\]]/.test(termo)) return null

  // NA PRIMEIRA COLUNA, EXIGE UMA LETRA. Sem isto, começar um cabeçalho ("# ") abriria a lista de
  // etiquetas no instante em que se digita o "#" — e a lista mais atrapalhadora é a que aparece quando
  // não se pediu nada. Com uma letra já não há ambiguidade: cabeçalho tem espaço, etiqueta não.
  if (jogo === linha.length - linha.trimStart().length && termo.length === 0) return null

  if (dentroDeBlocoDeCodigo(cm, cur.line)) return null

  return { termo, de: { line: cur.line, ch: jogo }, ate: cur }
}

/** Contagem de cercas acima da linha. Ímpar = estamos dentro de um bloco ainda aberto. */
function dentroDeBlocoDeCodigo(cm, ate) {
  let cercas = 0
  for (let i = 0; i < ate; i++)
    if ((cm.getLine(i) ?? '').trimStart().startsWith('```')) cercas++
  return cercas % 2 === 1
}

const completarLigacao = (ouvinte) => ({
  contexto: contextoDeLigacao,
  // Depois das sugestões reais, a saída que o Obsidian ensinou: CRIAR. Quando a nota que a pessoa quer
  // não existe (ou nada casa), o Enter não pode linkar um palpite — ele oferece "[[termo]]" como link
  // por escrever, que é exatamente o que a coluna "Ainda por escrever" sabe transformar em nota depois.
  buscar: (termo) => ouvinte.invokeMethodAsync('AoCompletarLigacao', termo).then((r) => {
    const lista = r ?? []
    const t = (termo ?? '').trim()
    if (t.length >= 2 && !lista.some((s) => (s.nome ?? '').toLowerCase() === t.toLowerCase()))
      lista.push({ criar: true, nome: t, pasta: null, insercao: t })
    return lista
  }),
  // A pasta é o que separa duas notas de mesmo nome; sem ela, escolher entre duas linhas iguais é sorte.
  item: (s) => s.criar
    ? { principal: `Criar “${s.nome}”`, secundario: 'ainda por escrever' }
    : { principal: s.nome, secundario: s.pasta },
  texto: (s) => `[[${s.insercao}]]`,
})

const completarEtiqueta = (ouvinte) => ({
  contexto: contextoDeEtiqueta,
  buscar: (termo) => ouvinte.invokeMethodAsync('AoCompletarEtiqueta', termo),
  item: (s) => ({ principal: `#${s.valor}`, secundario: s.uso }),
  // SEM ESPAÇO NO FIM, de propósito: "#direito" é quase sempre o começo de "#direito/tributário", e um
  // espaço automático obrigaria a apagá-lo para continuar descendo na hierarquia.
  texto: (s) => `#${s.valor}`,
})

// ——————————————————————————————————————————————————————————————————————————————————————————
// COMPLETAR PALAVRA PELO PRÓPRIO TEXTO DA NOTA
//
// O QUE ELE RESOLVE: resumo de estudo repete palavra comprida. "Inconstitucionalidade",
// "hipossuficiência", "responsabilidade objetiva" — quem escreve sobre um assunto escreve o nome dele
// dezenas de vezes na mesma nota, e é digitação pura: nada a decidir, só letras a repetir. Aqui basta o
// começo, e o resto vem do que já está escrito.
//
// O DICIONÁRIO É A PRÓPRIA NOTA, e não o vault inteiro. É a decisão de desenho deste completador, e há
// duas razões:
//
//   1. As palavras que se está repetindo AGORA estão na nota aberta. É onde a dor está, e sai de graça:
//      zero ida ao servidor, zero espera, funciona offline.
//   2. O completador de alcance-vault JÁ EXISTE e chama-se "[[". Fazer este buscar no vault criaria um
//      segundo caminho para a mesma coisa, com outra pontuação e outro resultado — e a pessoa deixaria
//      de saber qual dos dois responde o quê.
//
// AS TRÊS TRAVAS QUE O IMPEDEM DE ATRAPALHAR. Este é o completador com mais chance de virar praga,
// porque o contexto dele é "qualquer palavra" — o contrário dos outros dois, que a pessoa CONVOCA
// digitando "[[" ou "#". Sem freio, ele pisca a cada palavra digitada:
//
//   PREFIXO MÍNIMO ... 2 letras. Uma só casaria com quase tudo; duas já é uma escolha.
//   PALAVRA MÍNIMA ... 5 letras. O corte é na PALAVRA, não no quanto ela poupa — e a diferença entre
//                      as duas coisas foi um defeito de verdade, descrito abaixo.
//   O TECLADO É DE QUEM ESCREVE. Enter é parágrafo, ↑/↓ movem o cursor. Nos outros dois completadores
//                      a lista foi CONVOCADA ("[[", "#") e sequestrar essas teclas é o esperado; aqui
//                      ela aparece sozinha no meio da frase, e roubar Enter ou as setas de quem está
//                      escrevendo transformaria a nota num campo minado. Só Tab aceita — e o mouse,
//                      que é como se escolhe um item que não seja o primeiro. Ver `aceitaEnter` e
//                      `navegaComSetas` em ligarCompletar.
//
// O CORTE É NA PALAVRA, E NÃO NO GANHO — E ISTO NASCEU DE UM DEFEITO. A primeira versão exigia que a
// sugestão poupasse 3 letras, o que parecia razoável e produzia uma lista que ENCOLHE À MEDIDA QUE SE
// DIGITA: com "ca" o "carro" aparecia; com "car" ele sumia, porque já não poupava 3. A pessoa vê a
// palavra que quer, digita mais uma letra na direção dela — e ela desaparece. É o pior comportamento
// possível numa lista de sugestão, porque pune exatamente quem está acertando.
//
// Com o corte na palavra a lista é MONÓTONA: ela só estreita. Nada que estava lá some por você ter
// chegado mais perto. A palavra sai da lista num caso só, e é o certo: quando você já a digitou
// inteira (a sugestão tem de ser estritamente mais longa que o prefixo, senão não há o que completar).
const MIN_PREFIXO = 2
const MIN_PALAVRA = 5
const MAX_PALAVRAS = 6

/** A palavra que está sendo digitada — quando não é assunto de nenhum dos outros dois completadores. */
function contextoDePalavra(cm) {
  // OS OUTROS DOIS MANDAM NO LUGAR DELES. Dentro de "[[" ou logo depois de "#", quem responde é o
  // completador de conteúdo; abrir os dois deixaria duas listas sobre o texto e Enter tratado duas vezes.
  if (contextoDeLigacao(cm) || contextoDeEtiqueta(cm)) return null

  const cur = cm.getCursor()
  const linha = cm.getLine(cur.line) ?? ''

  // NO MEIO DE UMA PALAVRA, NÃO. Corrigir uma letra lá no meio de "responsabilidade" abriria a lista
  // para completar o que já está completo — e a sugestão sobrescreveria a segunda metade da palavra.
  if (/[\p{L}\p{N}]/u.test(linha[cur.ch] ?? '')) return null

  const casa = linha.slice(0, cur.ch).match(/[\p{L}][\p{L}\p{N}]*$/u)
  if (!casa || casa[0].length < MIN_PREFIXO) return null

  return { termo: casa[0], de: { line: cur.line, ch: cur.ch - casa[0].length }, ate: cur }
}

/**
 * As palavras da nota que continuam o prefixo, das mais usadas para as menos.
 *
 * A CONTAGEM É POR MINÚSCULA, mas o que se insere é a forma como ela aparece escrita: "Prescrição" e
 * "prescrição" são a mesma palavra para contar, e seriam duas linhas iguais na lista se não fossem
 * juntadas. Já a capitalização de QUEM DIGITA vence a do texto — quem começou a frase com "Presc"
 * quer "Prescrição", ainda que na nota ela apareça sempre em minúscula no meio das frases.
 */
function palavrasQueContinuam(texto, prefixo) {
  const alvo = prefixo.toLowerCase()
  const vistas = new Map()

  for (const [palavra] of texto.matchAll(/\p{L}[\p{L}\p{N}]*/gu)) {
    // Palavra curta não vale a lista: "gato" se digita mais rápido do que se escolhe.
    if (palavra.length < MIN_PALAVRA) continue
    // Estritamente mais longa que o prefixo — completar o que já está completo não completa nada.
    if (palavra.length <= prefixo.length) continue
    const chave = palavra.toLowerCase()
    if (!chave.startsWith(alvo)) continue
    const j = vistas.get(chave)
    if (j) j.n++
    else vistas.set(chave, { forma: palavra, n: 1 })
  }

  return [...vistas.values()]
    // Mais usada primeiro (é a que a nota está tratando). Empate: a MAIS LONGA.
    //
    // O desempate já foi o contrário — "a mais curta, que compromete menos" — e estava errado por um
    // motivo que só se vê olhando a lista pronta: com o teto de MAX_PALAVRAS, preferir as curtas
    // EXPULSA as longas. Numa nota com "carro, campo, carga, cachorro, cadastro, Caderno, carambola,
    // caracterização", digitar "Ca" mostrava as seis curtinhas e deixava de fora as duas únicas que
    // valia a pena completar. Palavra de cinco letras se digita mais rápido do que se escolhe numa
    // lista; a de catorze é o motivo de tudo isto existir.
    .sort((a, b) => b.n - a.n || b.forma.length - a.forma.length || a.forma.localeCompare(b.forma, 'pt'))
    .slice(0, MAX_PALAVRAS)
    .map((p) => ({ palavra: p.forma, usos: p.n, insercao: prefixo + p.forma.slice(prefixo.length) }))
}

const completarPalavra = () => ({
  aceitaEnter: false,
  navegaComSetas: false,
  contexto: contextoDePalavra,
  // Local e síncrono — mas devolve promessa porque o mecanismo é o mesmo dos que vão ao servidor. Varrer
  // a nota inteira a cada tecla parece caro e não é: são microssegundos numa nota de dezenas de milhares
  // de caracteres, e a espera de 90 ms do mecanismo já limita a frequência.
  buscar: (termo, cm) => Promise.resolve(palavrasQueContinuam(cm.getValue(), termo)),
  item: (s) => ({ principal: s.palavra, secundario: s.usos > 1 ? `${s.usos}×` : null }),
  texto: (s) => s.insercao,
})

function ligarCompletar(cm, opcoes) {
  let caixa = null
  let sugestoes = []
  let escolhido = 0
  let pedido = null
  // Onde acabamos de aceitar. Sem isto, inserir "#direito" moveria o cursor, o cursorActivity dispararia
  // de novo e a lista reabriria mostrando a etiqueta que acabou de ser escolhida.
  let recemAceito = null

  const fechar = () => {
    clearTimeout(pedido)
    caixa?.remove()
    caixa = null
    sugestoes = []
  }

  const contexto = () => opcoes.contexto(cm)

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
      const { principal, secundario } = opcoes.item(s)
      const item = document.createElement('div')
      item.className = 'cm-ligacoes-item' + (i === escolhido ? ' cm-ligacoes-atual' : '')
      const nome = document.createElement('span')
      nome.className = 'cm-ligacoes-nome'
      nome.textContent = principal
      item.appendChild(nome)
      if (secundario) {
        const lado = document.createElement('span')
        lado.className = 'cm-ligacoes-pasta'
        lado.textContent = secundario
        item.appendChild(lado)
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

    cm.replaceRange(opcoes.texto(escolha), ctx.de, ctx.ate)
    recemAceito = { line: ctx.de.line, ch: ctx.de.ch }
    fechar()
    cm.focus()
  }

  const reavaliar = () => {
    const ctx = contexto()
    if (!ctx) { recemAceito = null; return fechar() }

    // Continua sendo o mesmo trecho que acabamos de preencher: não reabrir. Assim que a pessoa mexer em
    // outro lugar — ou continuar digitando a partir de outro "#" —, a supressão cai sozinha.
    if (recemAceito && recemAceito.line === ctx.de.line && recemAceito.ch === ctx.de.ch) return fechar()
    recemAceito = null

    clearTimeout(pedido)
    pedido = setTimeout(() => {
      opcoes.buscar(ctx.termo, cm)
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
      // ↑/↓ SÓ ANDAM NA LISTA ONDE A LISTA FOI CONVOCADA. Ver o bloco de `completarPalavra`: aquela
      // lista abre com duas letras, ou seja, fica aberta quase o tempo todo enquanto se escreve —
      // e roubar as setas dali faria a seta parar de mover o cursor no meio de uma nota. Fecha e
      // deixa passar; quem quiser um item que não o primeiro escolhe com o mouse.
      case 'ArrowDown':
      case 'ArrowUp':
        if (opcoes.navegaComSetas === false) { fechar(); return }
        mover(e.key === 'ArrowDown' ? 1 : -1); break
      // ENTER ACEITA SÓ ONDE A LISTA FOI CONVOCADA ("[[", "#"). O completador de palavra aparece
      // sozinho no meio da frase, e ali Enter quer dizer PARÁGRAFO NOVO — sequestrá-lo faria a nota
      // ganhar uma palavra completada toda vez que se muda de linha. Fecha e deixa o Enter passar
      // (é ele quem continua a lista de marcadores, em extraKeys).
      case 'Enter':
        if (opcoes.aceitaEnter === false) { fechar(); return }
        aceitar(escolhido); break
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

export function destruir(id, geracaoEsperada) {
  const e = editores.get(id)
  if (!e) return

  // O CRACHÁ. Sem esta linha, o descarte atrasado de uma tela que já saiu apaga o editor da tela que
  // acabou de entrar — ver o comentário da geração, acima. Zero e indefinido querem dizer "destrua o
  // que estiver aí", que é o que se quer quando ninguém chegou a criar nada.
  if (geracaoEsperada && e.geracao !== geracaoEsperada) return

  e.limpar()
  // O editor vive no DOM do navegador; sem limpar, cada troca de nota deixa um CodeMirror órfão segurando
  // memória. Numa sessão de estudo de horas, isso é o navegador engasgando.
  e.cm.getWrapperElement()?.remove()
  editores.delete(id)
}
