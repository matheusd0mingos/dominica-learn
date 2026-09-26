import { test, expect, type Page, type Browser } from '@playwright/test'

// O APELIDO — a identidade do produto, e a coisa mais fácil de estragar sem perceber.
//
// Ele não é um nome de exibição: é o NOME DA PASTA do vault em disco e o valor da coluna `Usuario` que
// filtra o registro inteiro. Duas contas com o mesmo apelido não ficam "parecidas" — elas compartilham
// as mesmas notas e os mesmos estudos, cada uma vendo os da outra.
//
// Dois fatos, e os dois vieram de perguntas de uso real:
//   1. ele precisa APARECER, senão o convite de "acompanhar meus estudos" pede uma chave que ninguém
//      tem como obter (a barra do topo mostra o e-mail, e o apelido só existia na Administração);
//   2. ele precisa ser ÚNICO, e a garantia tem de ser do banco — a verificação em C# do cadastro é um
//      "consulta e depois insere", e entre a pergunta e a criação cabe outro cadastro.
test.describe.configure({ mode: 'serial' })
test.beforeEach(() => test.setTimeout(120_000))
test.use({ storageState: { cookies: [], origins: [] } })

const CARIMBO = Date.now()
const EU = { apelido: `perfil${CARIMBO}`.slice(0, 20), email: `perfil${CARIMBO}@dominica.local` }
const FRASE = 'frase secreta do teste e2e'

test('o apelido aparece no perfil, e a tela diz que ele não muda', async ({ browser }) => {
  const page = await entrar(browser, EU)
  try {
    await page.goto('/Account/Manage')
    const campo = page.locator('#apelido')
    await expect(campo, 'o perfil não mostra o apelido').toBeVisible()
    await expect(campo).toHaveValue(EU.apelido)

    // IMUTÁVEL, E DITO. Um campo cinza sem explicação se lê como defeito ("por que não me deixa
    // editar?"); com a razão ao lado, se lê como decisão — e a razão é real: o apelido É o caminho no
    // disco, trocá-lo significa mover a árvore inteira e reescrever o índice.
    await expect(campo).toBeDisabled()
    await expect(page.getByText(/não muda.*nome da pasta/i)).toBeVisible()
  } finally {
    await page.context().close()
  }
})

// APELIDO REPETIDO É RECUSADO — e o teste vale por causa do que ele protege, não pela mensagem.
test('cadastrar com um apelido que já existe é recusado', async ({ browser }) => {
  const ctx = await browser.newContext()
  const page = await ctx.newPage()
  try {
    await page.goto('/Account/Register')
    await page.locator('input[name="Input.Apelido"]').fill(EU.apelido)   // o mesmo do teste acima
    await page.locator('input[name="Input.Email"]').fill(`outro${CARIMBO}@dominica.local`)
    await page.locator('input[name="Input.Password"]').fill(FRASE)
    await page.locator('input[name="Input.ConfirmPassword"]').fill(FRASE)
    await page.getByRole('button', { name: /criar|registrar|cadastrar/i }).first().click()
    await page.waitForLoadState('networkidle')

    // DUAS MENSAGENS, UMA RECUSA. Quem barra pode ser a checagem antecipada do formulário ("já está em
    // uso") ou o índice único do banco, quando outro cadastro tomou o apelido no meio ("acabou de ser
    // registrado"). O teste aceita as duas de propósito: o que ele guarda é a RECUSA, não qual das duas
    // redes a pegou — e é assim que ele continua provando a garantia se um dia a checagem em C# sair.
    await expect(page.getByText(/já está em uso|acabou de ser registrado/i),
      'o cadastro aceitou um apelido repetido').toBeVisible()
    expect(await logado(page), 'a conta duplicada foi criada mesmo assim').toBeFalsy()
  } finally {
    await ctx.close()
  }
})

// A MAIÚSCULA NÃO CRIA UM SEGUNDO APELIDO. O domínio normaliza para minúsculas (ApelidoDoUsuario), e é
// isso que impede "Matheus" e "matheus" de virarem duas contas que, num sistema de arquivos que não
// diferencia caixa, apontariam para a MESMA pasta — duas contas, um vault, sem ninguém perceber.
test('o mesmo apelido em maiúsculas também é recusado', async ({ browser }) => {
  const ctx = await browser.newContext()
  const page = await ctx.newPage()
  try {
    await page.goto('/Account/Register')
    await page.locator('input[name="Input.Apelido"]').fill(EU.apelido.toUpperCase())
    await page.locator('input[name="Input.Email"]').fill(`maiusc${CARIMBO}@dominica.local`)
    await page.locator('input[name="Input.Password"]').fill(FRASE)
    await page.locator('input[name="Input.ConfirmPassword"]').fill(FRASE)
    await page.getByRole('button', { name: /criar|registrar|cadastrar/i }).first().click()
    await page.waitForLoadState('networkidle')

    await expect(page.getByText(/já está em uso|acabou de ser registrado/i),
      'a caixa alta criou um segundo apelido').toBeVisible()
  } finally {
    await ctx.close()
  }
})

// ——— apoio ———

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
