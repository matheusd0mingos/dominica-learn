import { test, expect, type Page, type Browser, type Locator } from '@playwright/test'

// TURMAS — o caminho que faz isto servir para uma sala, e não só para dois amigos.
//
// A turma INVERTE o convite: o professor cria uma vez e dita o código; cada aluno entra. O que ela NÃO
// muda é quem concede — entrar grava um acompanhamento normal, do vault do aluno para o professor, que
// o aluno revoga no painel dele quando quiser.
//
// O QUE SÓ AQUI SE PROVA é o encadeamento inteiro em duas sessões de verdade: criar, ditar, entrar,
// aparecer na lista, abrir o painel — e o aviso, que é o que faz o convidado saber que existe algo
// para ver.
test.describe.configure({ mode: 'serial' })
test.beforeEach(() => test.setTimeout(180_000))
test.use({ storageState: { cookies: [], origins: [] } })

const CARIMBO = Date.now()
const PROF = { apelido: `mestre${CARIMBO}`.slice(0, 20), email: `mestre${CARIMBO}@dominica.local` }
const ALUNO = { apelido: `pupilo${CARIMBO}`.slice(0, 20), email: `pupilo${CARIMBO}@dominica.local` }
const FRASE = 'frase secreta do teste e2e'
const NOME_DA_TURMA = `Contabilidade ${CARIMBO}`

let codigo = ''

test('o professor cria a turma e o aluno entra pelo código', async ({ browser }) => {
  const prof = await entrar(browser, PROF)
  const aluno = await entrar(browser, ALUNO)

  try {
    // —— O PROFESSOR CRIA ——
    await prof.goto('/turmas')
    await digitarQuandoVivo(
      prof.getByLabel('Nome da turma'), NOME_DA_TURMA,
      prof.getByRole('button', { name: /^Criar$/ }))
    await clicarAte(
      prof.getByRole('button', { name: /^Criar$/ }),
      prof.getByText(NOME_DA_TURMA).first())

    // o código aparece na lista; é ele que o professor dita
    const linha = await prof.getByText(/^código [A-Z0-9]{8}$/).first().innerText()
    codigo = linha.replace('código ', '').trim()
    expect(codigo, 'não achei o código da turma na tela').toMatch(/^[A-Z0-9]{8}$/)

    // —— O ALUNO ENTRA, e VÊ DE QUEM É ANTES DE CONFIRMAR ——
    // O código circula por voz e por grupo; entrar direto seria abrir o próprio painel para quem quer
    // que o tenha inventado. A confirmação existe para transformar isso numa decisão.
    await aluno.goto('/turmas')
    await digitarQuandoVivo(
      aluno.getByLabel('Código da turma'), codigo,
      aluno.getByRole('button', { name: /^Entrar$/ }))
    await clicarAte(
      aluno.getByRole('button', { name: /^Entrar$/ }),
      aluno.locator('div[role=dialog]'))

    const dialogo = aluno.locator('div[role=dialog]')
    await expect(dialogo).toContainText(NOME_DA_TURMA)
    await expect(dialogo, 'a confirmação precisa dizer DE QUEM é a turma').toContainText(PROF.apelido)
    await dialogo.getByRole('button', { name: /^Entrar$/ }).click()
    await expect(dialogo).toHaveCount(0, { timeout: 20_000 })

    // a turma passa a aparecer em "Você está em"
    await expect(aluno.getByText(NOME_DA_TURMA).first()).toBeVisible()

    // —— O ACESSO EXISTE DE VERDADE: aparece no painel do ALUNO como quem o acompanha ——
    await aluno.getByRole('link', { name: 'Painel', exact: true }).click()
    await expect(aluno.locator('.mud-chip', { hasText: PROF.apelido })).toBeVisible({ timeout: 20_000 })

    // —— E O PROFESSOR VÊ O ALUNO NA LISTA DA TURMA, com acesso ativo ——
    await prof.goto(`/turmas/${codigo}`)
    await expect(prof.getByText(ALUNO.apelido).first()).toBeVisible()
    await expect(prof.getByText('ativo').first()).toBeVisible()
  } finally {
    await prof.context().close()
    await aluno.context().close()
  }
})

// O AVISO É O QUE FAZ O CONVITE EXISTIR. Sem ele, quem ganha acesso não tem motivo nenhum para abrir a
// tela de acompanhamento — o professor acha que o aluno ignorou, e o aluno nunca soube.
test('quem ganha acesso vê um selo na barra, e ele some depois de olhar', async ({ browser }) => {
  const prof = await entrar(browser, PROF)
  try {
    // o professor foi convidado pelo aluno (foi o que a entrada na turma fez), e ainda não olhou
    await prof.goto('/')
    await expect(prof.locator('.nav-selo'), 'o selo de novidade não apareceu').toBeVisible()

    await prof.getByRole('link', { name: /Acompanhando/ }).click()
    await expect(prof.getByText(ALUNO.apelido).first()).toBeVisible()

    // VISTO AO ABRIR A LISTA, e não ao abrir um painel: quem chegou até aqui já sabe que há algo.
    await prof.getByRole('link', { name: 'Painel', exact: true }).click()
    await expect(prof.locator('.nav-selo')).toHaveCount(0)
  } finally {
    await prof.context().close()
  }
})

// ENCERRAR A TURMA TEM DE SIGNIFICAR "NÃO VEJO MAIS". Apagar só o agrupamento deixaria o professor
// vendo os painéis sem nenhuma lista onde isso apareça — nem ele nem os alunos notariam.
test('encerrar a turma faz o professor deixar de ver os painéis dela', async ({ browser }) => {
  const prof = await entrar(browser, PROF)
  try {
    await prof.goto(`/turmas/${codigo}`)
    await clicarAte(
      prof.getByRole('button', { name: /Encerrar turma/ }),
      prof.locator('div[role=dialog]'))
    await prof.locator('div[role=dialog]').getByRole('button', { name: /^Encerrar$/ }).click()

    // a turma some da lista…
    await expect(prof.getByText(NOME_DA_TURMA)).toHaveCount(0, { timeout: 20_000 })
    // …e o acesso ao painel do aluno vai junto
    await prof.goto(`/acompanhando/${ALUNO.apelido}/estudo`)
    await expect(prof.getByText('Você não tem acesso a estes estudos', { exact: false })).toBeVisible()
  } finally {
    await prof.context().close()
  }
})

// ——— apoio (ver e2e/README: pré-render engole o primeiro clique) ———

async function entrar(browser: Browser, quem: { apelido: string; email: string }): Promise<Page> {
  const page = await (await browser.newContext()).newPage()
  await page.goto('/Account/Login')
  await page.locator('input[name="Input.Email"]').fill(quem.email)
  await page.locator('input[name="Input.Password"]').fill(FRASE)
  await page.getByRole('button', { name: /^entrar$/i }).first().click()
  await page.waitForLoadState('networkidle')

  if (!(await logado(page))) {
    await page.goto('/Account/Register')
    await page.locator('input[name="Input.Apelido"]').fill(quem.apelido)
    await page.locator('input[name="Input.Email"]').fill(quem.email)
    await page.locator('input[name="Input.Password"]').fill(FRASE)
    await page.locator('input[name="Input.ConfirmPassword"]').fill(FRASE)
    await page.getByRole('button', { name: /criar|registrar|cadastrar/i }).first().click()
    await page.waitForLoadState('networkidle')
  }
  expect(await logado(page), `não entrei como ${quem.apelido}`).toBeTruthy()
  return page
}

async function logado(page: Page) {
  await page.goto('/')
  return (await page.getByRole('link', { name: /^sair$/i }).count()) > 0
      || (await page.getByRole('button', { name: /^sair$/i }).count()) > 0
}

async function clicarAte(botao: Locator, efeito: Locator) {
  await expect(async () => {
    await botao.click()
    await expect(efeito.first()).toBeVisible({ timeout: 2_000 })
  }).toPass({ timeout: 30_000 })
}

async function digitarQuandoVivo(campo: Locator, valor: string, habilita: Locator) {
  await expect(async () => {
    await campo.fill(valor)
    await expect(habilita).toBeEnabled({ timeout: 2_000 })
  }).toPass({ timeout: 30_000 })
}
