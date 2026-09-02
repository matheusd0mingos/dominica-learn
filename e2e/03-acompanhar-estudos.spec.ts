import { test, expect, type Page, type Browser, type Locator } from '@playwright/test'

// ACOMPANHAR OS ESTUDOS DE OUTRA PESSOA — com duas contas de verdade, em duas sessões.
//
// Estes testes NÃO usam o storageState do setup: precisam de dois usuários ao mesmo tempo, e é
// justamente a fronteira entre eles que está sendo provada. Cada um abre o seu contexto.
//
// O QUE SÓ AQUI SE PROVA, e que nenhum teste de unidade alcança: que ler o painel alheio não CONTAMINA
// a sessão de quem leu. O mecanismo abre um escopo de injeção próprio; se um dia alguém trocar isso
// pelo escopo do circuito — que no Blazor Server é a aba inteira — o professor passaria a ver o vault
// do aluno em "Notas" e a gravar nele. Não daria erro nenhum. Daria os dados errados, calados.
// SERIAL E COM MAIS FÔLEGO. Cada teste daqui cria contas de verdade, espera o circuito do Blazor
// conectar duas vezes e navega entre duas sessões — passa dos 90 s padrão sem nada estar errado. Um
// timeout apertado num teste legítimo é pior que um teste lento: ele falha por motivo aleatório e a
// primeira reação de quem vê é desconfiar do código certo.
test.describe.configure({ mode: 'serial' })
test.beforeEach(() => test.setTimeout(180_000))
test.use({ storageState: { cookies: [], origins: [] } })

const CARIMBO = Date.now()
const ALUNO = { apelido: `aluno${CARIMBO}`.slice(0, 20), email: `aluno${CARIMBO}@dominica.local` }
const PROF = { apelido: `prof${CARIMBO}`.slice(0, 20), email: `prof${CARIMBO}@dominica.local` }
const FRASE = 'frase secreta do teste e2e'

// Números escolhidos para serem reconhecíveis na tela e impossíveis de confundir com os de outra
// conta: 40 questões, 30 acertos (75%).
const QUESTOES = 40
const ACERTOS = 30

test('o dono abre o acesso, o convidado vê os números — e a conta de quem olha continua dele', async ({ browser }) => {
  const aluno = await entrar(browser, ALUNO)
  const prof = await entrar(browser, PROF)

  try {
    // O ALUNO REGISTRA ESTUDO e abre o acesso para o professor.
    await registrarQuestoes(aluno, QUESTOES, ACERTOS)
    await aluno.goto('/painel')
    await digitarQuandoVivo(
      aluno.getByLabel('Apelido de quem vai acompanhar'), PROF.apelido,
      aluno.getByRole('button', { name: /Dar acesso/i }))
    await clicarAte(
      aluno.getByRole('button', { name: /Dar acesso/i }),
      aluno.locator('.mud-chip', { hasText: PROF.apelido }))

    // O PROFESSOR VÊ o aluno na lista e abre o painel dele.
    await prof.goto('/acompanhando')
    await clicarAte(
      prof.getByText(ALUNO.apelido, { exact: false }).first(),
      prof.getByText(`Estudos de ${ALUNO.apelido}`))
    await expect(prof.getByText('somente leitura', { exact: false })).toBeVisible()
    // os números do ALUNO, e não zeros
    await expect(prof.getByText(String(QUESTOES), { exact: true }).first()).toBeVisible()
    await expect(prof.getByText('75%', { exact: false }).first()).toBeVisible()

    // —— A ASSERÇÃO QUE JUSTIFICA O ESCOPO PRÓPRIO ——
    //
    // Depois de ler o painel do aluno, a sessão do professor tem de continuar sendo a DELE.
    //
    // NAVEGA PELO MENU, E NÃO COM goto — e esta linha é a diferença entre um teste que guarda o
    // desenho e um que só parece guardar. `goto` recarrega a página, e recarregar cria um CIRCUITO
    // NOVO: qualquer contaminação do escopo anterior morre junto, e o teste passa mesmo com o defeito
    // em pé. Medido: com a leitura trocada de propósito para usar o escopo do circuito, a versão com
    // `goto` passava. Clicar no menu mantém o mesmo circuito — que é onde a contaminação viveria.
    await prof.getByRole('link', { name: 'Painel', exact: true }).click()
    await expect(prof.getByText('Quem acompanha meus estudos')).toBeVisible()
    // o painel do professor é o DELE: ninguém o acompanha, e ele não tem as 40 questões do aluno
    await expect(prof.getByText('Ninguém ainda', { exact: false })).toBeVisible()
    await expect(prof.getByText(`${QUESTOES}`, { exact: true })).toHaveCount(0)
  } finally {
    await aluno.context().close()
    await prof.context().close()
  }
})

test('sem acesso, o painel alheio é negado mesmo digitando a URL', async ({ browser }) => {
  const intruso = await entrar(browser, { apelido: `xereta${CARIMBO}`.slice(0, 20), email: `xereta${CARIMBO}@dominica.local` })
  try {
    // A URL é adivinhável — o apelido é público entre quem se conhece. É a autorização que segura,
    // não a obscuridade do endereço.
    await intruso.goto(`/acompanhando/${ALUNO.apelido}/estudo`)
    await expect(intruso.getByText('Você não tem acesso a estes estudos', { exact: false })).toBeVisible()
    // e nenhum número do aluno chegou à tela
    await expect(intruso.getByText('75%', { exact: false })).toHaveCount(0)
  } finally {
    await intruso.context().close()
  }
})

test('revogar fecha a porta para quem já estava dentro', async ({ browser }) => {
  const aluno = await entrar(browser, ALUNO)
  const prof = await entrar(browser, PROF)
  try {
    await aluno.goto('/painel')
    // o chip do convidado tem um "x" — tirar acesso não pede confirmação, é a ação segura do par
    await clicarAte(
      aluno.locator('.mud-chip', { hasText: PROF.apelido }).locator('button'),
      aluno.getByText('Ninguém ainda', { exact: false }))

    // O PROFESSOR JÁ ESTAVA COM A PÁGINA ABERTA: recarregar tem de fechar. A permissão é conferida no
    // ato da leitura, não quando a lista foi montada.
    await prof.goto(`/acompanhando/${ALUNO.apelido}/estudo`)
    await expect(prof.getByText('Você não tem acesso a estes estudos', { exact: false })).toBeVisible()
  } finally {
    await aluno.context().close()
    await prof.context().close()
  }
})

// O APELIDO PRECISA ESTAR NA TELA — senão o campo ao lado pede um dado que ninguém consegue obter.
//
// O card pergunta o apelido DA OUTRA PESSOA. O apelido não aparecia em tela nenhuma fora da
// Administração (a barra do topo mostra o e-mail), então convidar dependia de as duas pessoas
// lembrarem do que digitaram no cadastro — a mesma dependência que o domínio da Turma critica por
// escrito. Um campo cuja chave é invisível não é um campo difícil: é um campo inútil.
test('o painel mostra o MEU apelido, que é o que se dita para ser convidado', async ({ browser }) => {
  const dono = await entrar(browser, ALUNO)
  try {
    await dono.goto('/painel')
    const meu = dono.locator('.apelido-meu')
    await expect(meu, 'o painel não mostra o próprio apelido em lugar nenhum').toBeVisible({ timeout: 20_000 })
    await expect(meu).toHaveText(ALUNO.apelido)

    // e ele fica ONDE a pergunta nasce: no mesmo card que pede o apelido do outro
    const card = dono.locator('.mud-paper', { has: dono.getByText('Quem acompanha meus estudos') })
    await expect(card.locator('.apelido-meu')).toHaveCount(1)
  } finally {
    await dono.context().close()
  }
})

// ——— apoio ———

/// Entra com a conta, cadastrando na primeira vez. Mesma razão de idempotência do auth.setup.
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

/// Registra um lote de questões pelo painel — é o gesto real, e o que popula o registro que o
/// professor vai ler.
///
/// A MATÉRIA VEM ANTES, e não é rodeio de teste: o diálogo de registro exige uma, porque o lote é
/// sempre DE alguma coisa. Uma conta recém-criada não tem nenhuma, então este é literalmente o
/// caminho de quem começa a usar o Learn hoje.
async function registrarQuestoes(page: Page, total: number, acertos: number) {
  await page.goto('/painel')

  const novaMateria = page.locator('div[role=dialog]')
  await clicarAte(page.getByRole('button', { name: 'Nova matéria' }), novaMateria)
  await novaMateria.locator('input').first().fill('Contabilidade')
  await novaMateria.getByRole('button', { name: /^criar$/i }).click()
  await expect(novaMateria).toHaveCount(0, { timeout: 20_000 })

  // ESPERA A MATÉRIA APARECER, e não só o diálogo fechar — foi a corrida que custou esta anotação.
  // O diálogo devolve o nome e fecha; a matéria só é CRIADA depois disso, no serviço. Abrir "Registrar
  // estudo" no intervalo pega um vault ainda sem matéria nenhuma, e o diálogo — que lê a lista uma vez,
  // ao abrir — fica preso em "Crie uma matéria primeiro" mesmo depois de ela passar a existir. O
  // screenshot da falha mostrava a matéria na tabela, atrás do diálogo que dizia não haver nenhuma.
  await expect(page.getByRole('cell', { name: 'Contabilidade' })).toBeVisible({ timeout: 20_000 })

  const registro = page.locator('div[role=dialog]')
  await clicarAte(page.getByRole('button', { name: 'Registrar estudo' }).first(), registro)
  await registro.getByLabel('Fiz').fill(String(total))
  await registro.getByLabel('Acertei').fill(String(acertos))
  await registro.getByRole('button', { name: /^registrar$/i }).click()
  await expect(registro).toHaveCount(0, { timeout: 20_000 })
}

/// CLICAR ATÉ O CIRCUITO ESTAR VIVO — a armadilha número um de testar Blazor Server.
///
/// A página chega PRÉ-RENDERIZADA: o HTML já tem o botão, com a aparência final, antes de o WebSocket
/// do circuito conectar. Um clique nessa janela é aceito pelo navegador e não faz absolutamente nada —
/// não há handler do outro lado ainda. O sintoma é cruel: o teste falha esperando um diálogo, e o
/// screenshot mostra a tela perfeita, com o botão bem ali.
///
/// Esperar `networkidle` não resolve (o WebSocket não conta) e um `waitForTimeout` fixo é aposta. O
/// que se espera aqui é o EFEITO do clique, que é a única prova de que havia alguém escutando.
/// DIGITAR ATÉ O BLAZOR OUVIR — a mesma armadilha do clique, do outro lado.
///
/// Um `fill` na janela do pré-render escreve no DOM e não dispara binding nenhum: o campo fica com o
/// texto na tela e o servidor continua achando que ele está vazio. O sintoma é um botão que segue
/// DESABILITADO ao lado de um campo visivelmente preenchido — e foi exatamente assim que apareceu aqui.
///
/// O sinal de que o servidor recebeu é o próprio botão habilitar, então é isso que se espera.
async function digitarQuandoVivo(campo: Locator, valor: string, habilita: Locator) {
  await expect(async () => {
    await campo.fill(valor)
    await expect(habilita).toBeEnabled({ timeout: 2_000 })
  }).toPass({ timeout: 30_000 })
}

async function clicarAte(botao: Locator, efeito: Locator) {
  await expect(async () => {
    await botao.click()
    await expect(efeito).toBeVisible({ timeout: 2_000 })
  }).toPass({ timeout: 30_000 })
}
