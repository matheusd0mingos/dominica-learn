import { test as setup, expect } from '@playwright/test'

// ENTRA UMA VEZ, e cria a conta se ela ainda não existir.
//
// IDEMPOTENTE DE PROPÓSITO, e não "registra sempre": o banco de identidade é o de desenvolvimento e
// sobrevive entre corridas. Um setup que só registra falharia da segunda vez em diante com "e-mail já
// em uso" — e o erro apontaria para o cadastro, não para o teste. Um setup que só entra falharia na
// primeira, num clone novo. Tenta entrar; se não entrou, cadastra e entra.
//
// O CADASTRO PRECISA ESTAR ABERTO — quem liga isso é o boot.mjs (Administracao__CadastroAberto=true).
// Se um dia esta etapa falhar num clone limpo, é ali que se olha primeiro.
const arquivoEstado = '.auth/estudante.json'

const EMAIL = 'e2e@dominica.local'
// 12+ caracteres: a política do Learn é frase secreta, não senha curta.
const FRASE = 'frase secreta do teste e2e'
const APELIDO = 'e2e'

setup('entra como o estudante do e2e (cadastrando na primeira vez)', async ({ page }) => {
  await page.goto('/Account/Login')
  await page.locator('input[name="Input.Email"]').fill(EMAIL)
  await page.locator('input[name="Input.Password"]').fill(FRASE)
  await page.getByRole('button', { name: /^entrar$/i }).first().click()
  await page.waitForLoadState('networkidle')

  if (!(await entrou(page))) {
    await page.goto('/Account/Register')
    await page.locator('input[name="Input.Apelido"]').fill(APELIDO)
    await page.locator('input[name="Input.Email"]').fill(EMAIL)
    await page.locator('input[name="Input.Password"]').fill(FRASE)
    await page.locator('input[name="Input.ConfirmPassword"]').fill(FRASE)
    await page.getByRole('button', { name: /criar|registrar|cadastrar/i }).first().click()
    await page.waitForLoadState('networkidle')
  }

  // A MENSAGEM DIZ ONDE OLHAR. Se isto falhar, não é o teste seguinte que está errado — é o login ou
  // o cadastro, e é o primeiro alarme que importa.
  expect(await entrou(page), 'não consegui entrar nem cadastrar — confira o Postgres e o CadastroAberto')
    .toBeTruthy()

  await page.context().storageState({ path: arquivoEstado })
})

// "Entrou" é ter o botão de sair na barra — e não a URL, que é / tanto para quem entrou quanto para
// quem foi devolvido à página inicial anônima.
async function entrou(page: import('@playwright/test').Page) {
  await page.goto('/')
  return (await page.getByRole('link', { name: /^sair$/i }).count()) > 0
      || (await page.getByRole('button', { name: /^sair$/i }).count()) > 0
}
