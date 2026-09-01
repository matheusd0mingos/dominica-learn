import { test, expect, type Page } from '@playwright/test'

// A NOTA PRECISA CABER NA TELA — e quando não cabe, precisa ROLAR, não sumir.
//
// Os dois defeitos que este arquivo prende vieram do uso real e são invisíveis para teste de
// endpoint: o servidor devolvia o HTML certo nos dois casos.
//
//   1. CONTEÚDO LARGO ERA CORTADO. Não havia regra de tabela nenhuma no app.css; uma tabela mais larga
//      que a coluna saía pela borda do cartão — sem barra de rolagem e sem pista de que havia mais
//      coluna ali. Medido antes da correção: tabela de 724px numa coluna de 616px, 142px passando do
//      pai e `scrollWidth > clientWidth` FALSO. Ou seja: corte, não rolagem escondida.
//
//   2. A NOTA VIVIA EM 6/12 DE TELA. Com navegação à esquerda e contexto à direita, sobra metade da
//      janela para o que a pessoa veio ler.
//
// A TABELA DO TESTE TEM OITO COLUNAS de propósito. Com cinco colunas curtas o texto quebra e cabe —
// foi o que aconteceu na primeira tentativa de reproduzir o defeito, e um teste que não reproduz o
// defeito é um teste que não guarda nada.
const NOTA = 'Testes E2E/tabela larga'
const CAMINHO = 'Testes E2E/tabela larga.md'

const TABELA = `
| competência | conta contábil | classificação completa | impacto no resultado do exercício | contrapartida usual no lançamento | onde aparece na DRE | fundamento legal | observação de prova |
|---|---|---|---|---|---|---|---|
| 2026-01 | juros ativos | receita financeira | positivo | banco conta movimento | receitas financeiras | Lei 6.404 art. 187 | cai muito em prova |
| 2026-02 | juros passivos | despesa financeira | negativo | empréstimos a pagar | despesas financeiras | Lei 6.404 art. 187 | atenção ao regime |
`

test.beforeEach(async ({ page }) => {
  await garantirNota(page)
})

test('conteúdo largo rola dentro do cartão, e não vaza nem corta', async ({ page }) => {
  await abrirParaLer(page)

  const tabela = page.locator('.nota-lida table').first()
  await expect(tabela).toBeVisible()

  // O QUE SE MEDE É A RELAÇÃO COM O PAI, e não uma largura fixa: largura em pixel depende de fonte,
  // zoom e versão do Chromium, e um número cravado aqui quebraria por motivo nenhum.
  const vazamento = await tabela.evaluate(e =>
    Math.round(e.getBoundingClientRect().right - e.closest('.nota-lida')!.getBoundingClientRect().right))
  expect(vazamento, 'a tabela está passando da área de leitura — é o corte que o CSS existe para evitar')
    .toBeLessThanOrEqual(1)

  // …e a página NÃO passa a rolar de lado, que seria trocar um problema por outro: o cartão inteiro,
  // o menu e a barra andariam junto com a tabela.
  const paginaRolaDeLado = await page.evaluate(() =>
    document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)
  expect(paginaRolaDeLado, 'a página passou a rolar na horizontal — o conteúdo largo deve rolar DENTRO da nota').toBe(false)
})

test('ampliar dá a largura toda à nota e esconde as duas colunas laterais', async ({ page }) => {
  await abrirParaLer(page)

  const editor = page.locator('.col-editor')
  const estreita = (await editor.boundingBox())!.width
  await expect(page.locator('.col-navegacao')).toHaveCount(1)

  await page.locator('button[aria-label="Ampliar a nota"]').click()

  // As colunas SOMEM do DOM, e não ficam escondidas por CSS: é isso que devolve o espaço ao editor.
  // Se um dia virarem `display:none`, a contagem continua 1 e a largura não cresce — e o teste avisa.
  await expect(page.locator('.col-navegacao')).toHaveCount(0)
  await expect(page.locator('.col-contexto')).toHaveCount(0)

  const larga = (await editor.boundingBox())!.width
  expect(larga, 'ampliar não aumentou a área de leitura').toBeGreaterThan(estreita * 1.5)

  // E a tabela de oito colunas passa a caber inteira — que é o ponto de ampliar, não um efeito colateral.
  const rolaAinda = await page.locator('.nota-lida table').first()
    .evaluate(e => e.scrollWidth > e.clientWidth + 1)
  expect(rolaAinda, 'mesmo ampliada a tabela ainda precisa rolar — a largura não chegou onde devia').toBe(false)

  // volta ao normal, para não deixar a preferência ligada para os testes seguintes (ela é guardada)
  await page.locator('button[aria-label="Voltar às três colunas"]').click()
  await expect(page.locator('.col-navegacao')).toHaveCount(1)
})

// ——— apoio ———

// Cria a nota se ela ainda não existir. O vault do e2e sobrevive entre corridas (é uma pasta em
// disco), então recriar sempre daria "já existe" na segunda vez.
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
  await page.keyboard.type('\n' + TABELA, { delay: 1 })
  // o autosave é por inatividade; sem esta espera a nota fecharia sem o conteúdo ter subido
  await expect(page.getByText(/salvo/i).first()).toBeVisible({ timeout: 20_000 })
}

async function abrirParaLer(page: Page) {
  await page.goto(`/notas/${CAMINHO.split('/').map(encodeURIComponent).join('/')}`)
  await page.locator('.CodeMirror').first().waitFor({ timeout: 30_000 })
  await page.getByText('Ler', { exact: true }).first().click()
  await expect(page.locator('.nota-lida table').first()).toBeVisible()
}
