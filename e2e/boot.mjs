// Sobe o Learn REAL para o E2E: Blazor Server, Postgres de verdade, vault próprio em disco.
//
// POR QUE NÃO HÁ "BUILD DO FRONT" AQUI, ao contrário do e2e da plataforma: o Learn não tem SPA. A tela
// é Blazor Server renderizado pelo próprio processo .NET, então subir o app JÁ é subir o front. O que
// existe de JavaScript (CodeMirror, mermaid) é arquivo estático servido de wwwroot.
//
// O VAULT É DEDICADO e fica em `e2e/.vault` (ignorado pelo git). O vault de desenvolvimento é o estudo
// de verdade de alguém; um teste que cria e apaga notas não pode morar lá dentro. Este é descartável
// por definição — apagá-lo é o jeito de "resetar" a suíte.
//
// O BANCO, AO CONTRÁRIO, É O DE DESENVOLVIMENTO — e isso é escolha, não descuido. O índice do Learn é
// DERIVADO do vault (a documentação manda reindexar quando algo está estranho), então o que os testes
// deixam no índice é reconstruível e some quando o vault some. Criar três bancos só para o e2e traria
// a parte cara (privilégio de criar banco, migração, limpeza) para resolver um problema que o vault
// dedicado já resolve. Quem quiser separar mesmo assim: as três variáveis abaixo aceitam override.
import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const e2e = dirname(fileURLToPath(import.meta.url))
const learn = resolve(e2e, '..')
const log = (m) => console.log(`[e2e-learn] ${m}`)

const env = {
  ...process.env,
  ASPNETCORE_URLS: 'http://127.0.0.1:5091',
  ASPNETCORE_ENVIRONMENT: 'Development',
  // O cadastro precisa estar aberto: é assim que o auth.setup cria o usuário do teste na primeira vez.
  Administracao__CadastroAberto: 'true',
  Vault__Raiz: resolve(e2e, '.vault'),
  // Ruído de log vira ruído de saída do Playwright — e o que importa aqui é a linha do teste.
  Logging__LogLevel__Default: 'Warning',
  ConnectionStrings__Indice: process.env.LEARN_E2E_INDICE
    ?? 'Host=localhost;Port=5432;Database=learn_indice;Username=learn;Password=learn',
  ConnectionStrings__Identidade: process.env.LEARN_E2E_IDENTIDADE
    ?? 'Host=localhost;Port=5432;Database=learn_identidade;Username=learn;Password=learn',
  ConnectionStrings__Registro: process.env.LEARN_E2E_REGISTRO
    ?? 'Host=localhost;Port=5432;Database=learn_registro;Username=learn;Password=learn',
}

log('subindo o Learn (Blazor Server + Postgres)…')
// stdio herdado: se o Postgres não estiver de pé, quem explica é a exceção do próprio app ("database
// does not exist" / "connection refused"), e ela precisa chegar aos olhos de quem rodou. Um boot que
// engole o erro e só estoura no timeout do Playwright manda a pessoa investigar o lugar errado.
const proc = spawn('dotnet', ['run', '--project', 'src/Dominica.Learn.Web/Dominica.Learn.Web.csproj',
  '-c', 'Release', '--no-launch-profile'], { cwd: learn, env, stdio: 'inherit' })

proc.on('exit', (code) => process.exit(code ?? 0))
for (const sinal of ['SIGINT', 'SIGTERM']) process.on(sinal, () => { try { proc.kill(sinal) } catch { /* já morreu */ } })
