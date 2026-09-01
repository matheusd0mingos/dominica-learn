import { test, expect, type Page } from '@playwright/test'

// BAIXAR A NOTA — e não conseguir baixar a de mais ninguém, nem nada fora do vault.
//
// A rota /nota.md monta um caminho de arquivo a partir de texto que veio da query. É a forma clássica
// de entregar /etc/passwd, e por isso metade deste arquivo é sobre o que ela RECUSA. Quem valida é o
// CaminhoNota, o mesmo tipo que valida caminho no resto do sistema — mas "está delegando para o tipo
// certo" é uma afirmação sobre o código, e o que se quer saber é o que a rota RESPONDE.
const NOTA = 'Testes E2E/exportar'
const CAMINHO = 'Testes E2E/exportar.md'
const CORPO = 'Conteúdo com acentuação: contábil, ação, três.'

test.beforeEach(async ({ page }) => {
  await garantirNota(page)
})

test('o menu da nota baixa o .md com o nome, o conteúdo e os acentos certos', async ({ page }) => {
  await page.goto(`/notas/${CAMINHO.split('/').map(encodeURIComponent).join('/')}`)
  await page.locator('.CodeMirror').first().waitFor({ timeout: 30_000 })

  await page.locator('.mud-menu button').last().click()
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.getByText('Baixar como .md').first().click(),
  ])

  // O NOME É O DA NOTA, sem a pasta: "exportar.md" é o que a pessoa espera na pasta de downloads —
  // não "Testes E2E_exportar.md" nem o caminho inteiro.
  expect(download.suggestedFilename()).toBe('exportar.md')

  const stream = await download.createReadStream()
  const baixado = (await new Response(stream as any).text())
  expect(baixado).toContain(CORPO)
  // O ACENTO É A ASSERÇÃO, não enfeite: sem UTF-8 explícito no content-type o navegador adivinha, e
  // "contábil" chega quebrado num arquivo que a pessoa vai guardar. Quebra é silenciosa.
  expect(baixado).toContain('contábil')
})

test('a rota recusa sair do vault e não conta o que existe lá dentro', async ({ page }) => {
  await page.goto('/')

  // fetch DE DENTRO DA PÁGINA, e não pelo request do Playwright: o cookie de sessão é SameSite, e uma
  // requisição sem página de origem não o carrega — o teste mediria o redirecionamento para o login e
  // passaria por motivo errado. Isto custou uma investigação; fica registrado.
  const pedir = (caminho: string) => page.evaluate(async (c) => {
    const r = await fetch('/nota.md?caminho=' + encodeURIComponent(c))
    return { status: r.status, corpo: (await r.text()).slice(0, 200) }
  }, caminho)

  const legitima = await pedir(CAMINHO)
  expect(legitima.status).toBe(200)

  for (const [caminho, rotulo] of [
    ['../../../etc/passwd', 'travessia com ../ no começo'],
    ['Testes E2E/../../../etc/passwd', 'travessia com ../ no meio'],
    ['/etc/passwd', 'caminho absoluto'],
  ] as const) {
    const r = await pedir(caminho)
    expect(r.status, `${rotulo} devia ser recusada`).toBeGreaterThanOrEqual(400)
    // A asserção que importa não é o número: é que o conteúdo do arquivo do sistema NÃO veio.
    expect(r.corpo, `${rotulo} VAZOU o arquivo`).not.toContain('root:')
  }

  // 404 e não 403 no inexistente, de propósito: a nota é procurada dentro do vault de quem pediu, e
  // distinguir "não existe" de "não é sua" contaria a quem chuta caminhos quais notas existem.
  expect((await pedir('Nada/inexistente.md')).status).toBe(404)
})

// ——— apoio ———

async function garantirNota(page: Page) {
  await page.goto(`/notas/${CAMINHO.split('/').map(encodeURIComponent).join('/')}`)
  await page.waitForLoadState('networkidle')
  if (await page.locator('.CodeMirror').count() > 0) return

  await page.goto('/notas')
  await page.getByRole('button', { name: /Nota nova/i }).first().click()
  await page.locator('div[role=dialog] input').first().fill(NOTA)
  await page.getByRole('button', { name: /^criar$/i }).click()

  const editor = page.locator('.CodeMirror').first()
  await editor.waitFor({ timeout: 30_000 })
  await editor.click()
  await page.keyboard.press('Control+End')
  await page.keyboard.type('\n' + CORPO, { delay: 1 })
  await expect(page.getByText(/salvo/i).first()).toBeVisible({ timeout: 20_000 })
}
