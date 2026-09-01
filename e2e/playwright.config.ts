import { defineConfig, devices } from '@playwright/test'
import { existsSync } from 'node:fs'

// E2E DO LEARN — o harness que faltava.
//
// Até aqui o Learn tinha suíte de domínio, de aplicação, de infraestrutura e de rotas, e NADA que
// abrisse a tela. O efeito disso não é teórico: os três defeitos que motivaram este arquivo (tabela
// cortada na leitura, nota espremida em 6/12 de tela, e a nota que não se conseguia exportar) são
// todos invisíveis para um teste de endpoint — o servidor respondia certo em todos eles.
//
// O QUE ENTRA AQUI: o que só se vê no navegador — layout que corta conteúdo, botão que não existe,
// download que não desce. O que se prova sem navegador continua nas suítes .NET, que rodam em três
// segundos; duplicar regra de domínio aqui seria pagar minutos por uma resposta que já se tem.
const PORTA = 5091
const BASE = `http://127.0.0.1:${PORTA}`

// Local: o Chromium pré-instalado do ambiente. CI: o do `playwright install chromium` (executablePath
// vazio → o Playwright acha sozinho). Override com PW_CHROMIUM. Mesma regra do e2e da plataforma.
const chromiumLocal = '/opt/pw-browsers/chromium-1194/chrome-linux/chrome'
const executablePath = process.env.PW_CHROMIUM
  || (process.env.CI ? undefined : (existsSync(chromiumLocal) ? chromiumLocal : undefined))

export default defineConfig({
  testDir: '.',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  // O Learn é Blazor SERVER: cada aba é um circuito com estado no servidor, e o vault é um só em
  // disco. Dois testes em paralelo mexendo no mesmo vault é corrida, não teste.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  use: {
    baseURL: BASE,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    // Entra UMA vez (criando a conta se ainda não existir) e guarda o cookie; os testes reaproveitam.
    { name: 'setup', testMatch: /auth\.setup\.ts/, use: { launchOptions: { executablePath, args: ['--no-sandbox'] } } },
    {
      name: 'chromium', dependencies: ['setup'], testIgnore: /auth\.setup\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        storageState: '.auth/estudante.json',
        launchOptions: { executablePath, args: ['--no-sandbox'] },
      },
    },
  ],
  webServer: {
    command: 'node boot.mjs',
    // /saude e não a raiz: a raiz responde 200 assim que o Kestrel sobe, ANTES de as migrações
    // terminarem — e o primeiro teste pegaria um app que ainda não sabe ler o vault.
    url: `${BASE}/saude`,
    reuseExistingServer: !process.env.CI,
    timeout: 240_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
})
