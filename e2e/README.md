# E2E do Learn

O que só o navegador enxerga. As suítes .NET (`dotnet test Dominica.Learn.slnx`) cobrem domínio,
aplicação, infraestrutura e rotas em ~3 segundos e continuam sendo o lugar certo para regra de
negócio — **não duplique regra aqui**. Aqui fica o que passa por elas sem ser notado.

Os três defeitos que criaram esta pasta são o argumento:

| Defeito | O que o servidor fazia |
|---|---|
| tabela larga cortada na leitura | devolvia o HTML certo — o corte era do CSS |
| nota espremida em 6/12 da tela | devolvia o HTML certo — faltava o botão de ampliar |
| não dava para exportar a nota | a rota nem existia, e nenhum teste sentia falta |

## Rodar

```bash
cd e2e
npm install          # uma vez por clone
npx playwright test
```

Precisa de **Postgres de pé** — o Learn é Blazor Server com banco, e o app migra sozinho no boot. Se
o Postgres estiver fora, a exceção do próprio app aparece na saída dizendo isso; é de propósito
(`boot.mjs` herda o stdio em vez de engolir o erro e estourar no timeout do Playwright).

O `boot.mjs` sobe o app na porta **5091** com:

- **vault dedicado** em `e2e/.vault` (ignorado pelo git) — o vault de desenvolvimento é o estudo de
  verdade de alguém, e teste que cria e apaga nota não mora lá dentro. Apagar essa pasta é o jeito de
  resetar a suíte;
- **cadastro aberto**, que é como o `auth.setup.ts` cria a conta do teste na primeira corrida;
- **os bancos de desenvolvimento**. Isso é escolha: o índice do Learn é *derivado* do vault, então o
  que os testes deixam nele é reconstruível e some junto com o vault. Para separar mesmo assim, use
  `LEARN_E2E_INDICE`, `LEARN_E2E_IDENTIDADE` e `LEARN_E2E_REGISTRO`.

## Como escrever um teste aqui

**Meça relação, não pixel.** `larguraDaTabela > larguraDaColuna` guarda o defeito; `largura === 724`
quebra quando muda a fonte, o zoom ou a versão do Chromium — e quebra sem ninguém ter errado nada.

**Reproduza o defeito antes de escrever a asserção.** A tabela do `01-leitura` tem oito colunas
porque com cinco colunas curtas o texto quebra e cabe: a primeira versão do teste passava com o
defeito em pé. Um teste que não reproduz o defeito não guarda nada.

**Cookie é `SameSite`.** O `request` do Playwright não tem página de origem e não o carrega — uma
requisição feita por ele mede o redirecionamento para o login e passa por motivo errado. Para bater
numa rota autenticada, use `page.evaluate(() => fetch(...))`, que sai de dentro da página. Isto custou
uma investigação; está anotado no `02-exportar`.

**O pré-render engole o primeiro clique.** A página chega como HTML estático antes de o WebSocket do
circuito conectar; um clique ou um `fill` nessa janela não dispara handler nenhum. O sintoma é cruel —
o teste falha esperando o efeito e o screenshot mostra a tela perfeita, com o botão bem ali, ou um
campo visivelmente preenchido ao lado de um botão que continua desabilitado. `networkidle` não ajuda
(o WebSocket não conta) e `waitForTimeout` é aposta. Use os helpers `clicarAte` / `digitarQuandoVivo`
do `03-acompanhar`: eles repetem a ação até o EFEITO aparecer, que é a única prova de que havia alguém
escutando do outro lado.

**`goto` apaga o circuito — e com ele, o defeito que você queria pegar.** A asserção mais importante do
`03-acompanhar` é que ler o painel de outra pessoa não contamina a sessão de quem leu. Escrita com
`page.goto('/painel')`, ela **passava com o defeito de propósito no lugar**: recarregar cria um circuito
novo, e a contaminação morre junto. Só clicando no menu — navegação dentro do mesmo circuito — o teste
passou a falhar quando devia. Para provar qualquer coisa sobre estado de circuito, navegue como o
usuário navega.

**Seja idempotente.** O vault e o banco sobrevivem entre corridas. Os specs criam a nota só se ela
ainda não existir, e o setup entra antes de tentar cadastrar.
