import { test, expect, type Page, type Locator } from '@playwright/test'

// O LINK PÚBLICO DE UMA NOTA — a única tela do Learn que abre sem conta.
//
// O QUE SÓ AQUI SE PROVA é justamente a parte que nenhuma suíte .NET alcança: que um navegador SEM
// COOKIE NENHUM abre o endereço e lê a nota. Os testes de aplicação provam que o serviço não pergunta
// quem está logado; eles não provam que a ROTA não exige autenticação — e essa é a metade que quebra
// calada, porque do lado de cá (logado) o link abre lindamente em qualquer cenário.
//
// O CONTEXTO ANÔNIMO É O TESTE. `browser.newContext()` sem storageState é um navegador limpo; usar a
// `page` da suíte (que carrega o cookie do estudante) faria o teste passar mesmo com [Authorize] na
// página — foi exatamente esse o erro que o e2e de acompanhamento pegou com o `page.goto`.
test.describe.configure({ mode: 'serial' })
test.beforeEach(() => test.setTimeout(180_000))

const CARIMBO = Date.now()
const NOTA = `Testes E2E/publicada ${CARIMBO}`
const CAMINHO = `${NOTA}.md`
const FRASE_DA_NOTA = `esta frase só existe na nota publicada ${CARIMBO}`

let url = ''

test('publicar uma nota entrega um endereço que abre sem login', async ({ page, browser }) => {
  await criarNota(page)

  // —— PUBLICAR ——
  await abrirMenuDaNota(page)
  await page.getByText(/Publicar num link/).click()

  // A CONFIRMAÇÃO PRECISA DIZER A CONSEQUÊNCIA. "Tem certeza?" não informa nada; o que a pessoa
  // precisa ler antes de clicar é que qualquer um com o endereço lê a nota sem ter conta.
  const confirmacao = page.locator('div[role=dialog]')
  await expect(confirmacao).toContainText(/sem ter conta/i)
  await confirmacao.getByRole('button', { name: /^Publicar$/ }).click()

  // —— O ENDEREÇO APARECE À MOSTRA, e não só atrás do botão "copiar" ——
  // Copiar falha por motivos que não são defeito nosso (contexto não seguro, permissão negada). Um
  // diálogo que só oferecesse o botão deixaria a pessoa sem saída nesses casos.
  const campo = page.locator('div[role=dialog] textarea, div[role=dialog] input[type=text]').first()
  await expect(campo).toBeVisible()
  url = (await campo.inputValue()).trim()
  expect(url, 'não achei o endereço do link no diálogo').toMatch(/\/n\/[A-Za-z0-9]{22}$/)

  await page.locator('div[role=dialog]').getByRole('button', { name: /^Fechar$/ }).click()

  // —— E AGORA O QUE IMPORTA: UM NAVEGADOR SEM COOKIE NENHUM ——
  const anonimo = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  try {
    const visita = await anonimo.newPage()
    const resposta = await visita.goto(url)

    expect(resposta?.status(), 'a rota pública não respondeu 200').toBe(200)
    // NÃO CAIU NO LOGIN. Sem esta asserção, uma página de login com a palavra certa no meio passaria.
    expect(visita.url(), 'o link redirecionou para o login — a rota está exigindo conta').toBe(url)
    await expect(visita.getByText(FRASE_DA_NOTA)).toBeVisible()

    // NÃO ENTRA EM BUSCADOR. "Não listado" não é "publicado no Google": basta um buscador seguir o
    // link de um e-mail encaminhado e o segredo acabou, para sempre.
    expect(resposta?.headers()['x-robots-tag'] ?? '', 'a nota publicada está indexável').toContain('noindex')

    // E O VISITANTE NÃO GANHA O RESTO DO VAULT junto: a barra do app, com Painel/Notas/Revisar, não
    // aparece — quem chegou aqui recebeu uma nota, não uma conta.
    await expect(visita.getByRole('link', { name: 'Painel', exact: true })).toHaveCount(0)
  } finally {
    await anonimo.close()
  }
})

test('o menu passa a mostrar que a nota está no ar', async ({ page }) => {
  await abrirNota(page)
  await abrirMenuDaNota(page)

  // O ESTADO TEM DE APARECER ANTES DE ABRIR O DIÁLOGO. "Compartilhar por link…" numa nota já publicada
  // esconderia justamente o fato que importa — que ela JÁ está lá fora.
  await expect(page.getByText(/Link público \(no ar\)/)).toBeVisible()
  await page.keyboard.press('Escape')
})

test('a lista do painel mostra o que foi publicado — é ela que torna publicar reversível', async ({ page }) => {
  await page.goto('/painel')

  // Sem esta lista, revogar dependeria de reabrir nota por nota procurando o item aceso no menu — o
  // que, num semestre de uso, é o mesmo que não poder revogar.
  await expect(page.getByText('Notas publicadas em link')).toBeVisible()
  await expect(page.getByText(CAMINHO, { exact: false })).toBeVisible()
})

test('revogar mata o endereço para quem já o tinha', async ({ page, browser }) => {
  await page.goto('/painel')
  const linha = page.locator('.mud-list-item', { hasText: CAMINHO })

  // ESPERA A LINHA EXISTIR ANTES DE QUALQUER COISA. Escrito sem isto, o teste passava de mentira: o
  // `toHaveCount(0)` logo abaixo é verdade enquanto a lista ainda nem renderizou, e o clique — engolido
  // pelo pré-render — não fazia nada. Foi o pre-push que pegou, com a linha ainda na tela no screenshot.
  await expect(linha).toHaveCount(1)

  // E o clique repete até fazer efeito, pela mesma razão de sempre (ver e2e/README): o Painel é
  // pré-renderizado, e clique antes de o circuito conectar é clique que não aconteceu.
  await clicarAte(
    linha.getByRole('button', { name: /^Revogar$/ }),
    page.getByText(/saiu do ar/))
  await expect(linha).toHaveCount(0)

  // O MESMO ENDEREÇO DE ANTES, no mesmo navegador limpo. O que se prova é que o link morre para quem
  // já o tinha guardado — se ele continuasse abrindo, "revogar" seria um botão decorativo.
  const anonimo = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  try {
    const visita = await anonimo.newPage()
    await visita.goto(url)
    await expect(visita.getByText(FRASE_DA_NOTA)).toHaveCount(0)
    await expect(visita.getByText(/não está disponível/i)).toBeVisible()
  } finally {
    await anonimo.close()
  }
})

// ——— apoio (ver e2e/README: o pré-render engole o primeiro clique) ———

async function criarNota(page: Page) {
  await page.goto('/notas')
  await clicarAte(
    page.getByRole('button', { name: /Nota nova/i }).first(),
    page.locator('div[role=dialog]'))

  await page.locator('div[role=dialog] input').first().fill(NOTA)
  await page.getByRole('button', { name: /^criar$/i }).click()

  const editor = page.locator('.CodeMirror').first()
  await editor.waitFor({ timeout: 30_000 })
  await editor.click()
  await page.keyboard.press('Control+End')
  await page.keyboard.type('\n' + FRASE_DA_NOTA, { delay: 1 })
  // O autosave é por inatividade. Publicar antes de o texto subir daria um link para uma nota vazia —
  // e o teste diria "publicou" sobre nada.
  await expect(page.getByText(/salvo/i).first()).toBeVisible({ timeout: 20_000 })
}

async function abrirNota(page: Page) {
  await page.goto(`/notas/${CAMINHO.split('/').map(encodeURIComponent).join('/')}`)
  await page.locator('.CodeMirror').first().waitFor({ timeout: 30_000 })
}

// DUAS ARMADILHAS DO MudMenu, as duas descobertas aqui:
//
//   • o `aria-label` fica no WRAPPER e não no botão — daí o `… button` no fim. Escrito como
//     `button[aria-label=…]`, nada casa, e o teste acusa "o menu não abriu" sobre um menu que está lá;
//
//   • os itens NÃO têm role=menuitem. O MudMenuItem renderiza um <p>, então `getByRole('menuitem')`
//     não acha nada mesmo com o menu aberto na tela. Por texto, acha.
async function abrirMenuDaNota(page: Page) {
  await clicarAte(
    page.locator('[aria-label="Mais ações desta nota"] button'),
    page.getByText(/Baixar como \.md/))
}

async function clicarAte(botao: Locator, efeito: Locator) {
  await expect(async () => {
    await botao.click()
    await expect(efeito.first()).toBeVisible({ timeout: 2_000 })
  }).toPass({ timeout: 30_000 })
}
