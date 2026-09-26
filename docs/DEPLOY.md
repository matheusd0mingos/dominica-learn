# Deploy do Dominica Learn

> **Dois jeitos de subir.** Sozinho, com o `docker-compose.yml` da raiz deste repositório — é o caminho
> para domínio próprio ou desenvolvimento, e está descrito em *Como rodar* no `README.md`. Ou junto da
> plataforma Dominica, no mesmo stack e atrás do mesmo Caddy — é o que o resto deste documento descreve.
> Os caminhos `novo/plataforma/...` citados abaixo são do repositório da plataforma, onde o Learn nasceu
> antes de ganhar repositório próprio.

## A pergunta primeiro: é o mesmo arquivo de deploy da plataforma?

**Agora é — o `novo/plataforma/deploy.sh` sobe os dois.**

Era preciso que fosse — e continua sendo depois da mudança para o subdomínio.
Quem encaminha a requisição de `learn.SEU_DOMINIO` é o **Caddy da plataforma**, e
o Caddy só alcança o contêiner do Learn se os dois estiverem na mesma rede do
Compose. Subdomínio muda o endereço, não a topologia: seguem sendo um stack só.

Juntar trouxe duas vantagens além dessa obrigação, e as duas contam num VPS pequeno:

1. **Um Postgres em vez de dois.** São ~200 MB de RAM e um alvo de backup a menos.
   Os três bancos do Learn (`learn_indice`, `learn_identidade`, `learn_registro`) seguem separados —
   compartilhar o **servidor** sem compartilhar o **esquema** é exatamente o que o
   próprio código já dizia fazer.
2. **Um `deploy.sh` só.** Dois roteiros de deploy é um roteiro que alguém esquece
   de rodar.

O compose próprio do Learn (`docker-compose.yml`, na raiz deste repositório) continua existindo e
válido: é o caminho para rodar o Learn **sozinho**, em domínio próprio ou em
desenvolvimento, sem a plataforma junto.

---

## Passo a passo — pondo o Learn na plataforma

**Isso já está feito.** O que segue descreve as peças e por que cada uma é como é;
para subir, pule para o passo 2.

### 1. As peças

| arquivo | o que faz |
|---|---|
| `novo/plataforma/docker-compose.learn.yml` | overlay com os serviços `learn` e `learn-bancos` |
| `novo/plataforma/Caddyfile` | o bloco de site `learn.{$DOMINICA_DOMAIN}` + o redirecionamento do caminho antigo |
| `novo/plataforma/deploy.sh` | pergunta, grava no `.env` e junta o overlay quando `LEARN=on` |

É **overlay opt-in**, no mesmo mecanismo do antivírus e do painel de métricas, e
não serviço fixo do compose de produção. A razão é dura: o Learn exige duas
variáveis novas (`VAULT_NO_HOST` e `LEARN_ADMIN_EMAIL`). Como serviço fixo, todo
deploy de plataforma já existente passaria a **abortar** por falta delas.

Três armadilhas que estavam neste documento **escritas erradas**, e que só
apareceriam em produção:

1. **O contexto do build é `../learn`, não a raiz do repo.** O `api` usa `../..`
   porque o Dockerfile dele precisa alcançar `novo/motor`. O Dockerfile do Learn
   copia `Directory.Build.props` e `src/Dominica.Learn.*` a partir da raiz do
   contexto — com o repo inteiro, aqueles caminhos não existem e o build quebra
   no `COPY`. Copiar a linha de um para o outro é o erro fácil aqui.

2. **O usuário do Postgres da plataforma é `dominica`, literal.** O compose base
   o fixa; não existe `POSTGRES_USER` no `.env` da plataforma. Usá-lo geraria uma
   string de conexão com o usuário vazio.

3. **`criar-bancos.sh` montado em `/docker-entrypoint-initdb.d/` NÃO funciona
   aqui.** Aquilo só roda quando o diretório de dados do Postgres está vazio, e o
   volume da plataforma já existe em qualquer deploy no ar — o script nunca
   rodaria e o Learn subiria batendo em *"database learn_indice does not exist"*.
   Por isso existe o serviço `learn-bancos`: um passo explícito, idempotente, que
   cria os três bancos e sai (o mesmo padrão do serviço `migrate` da plataforma).
   O `deploy.sh` ainda **confere no Postgres** quais existem depois de subir, e
   cria o que faltar — porque "o `learn-bancos` concluiu" pode ser verdade a
   respeito de uma lista de bancos mais curta, de uma versão anterior do arquivo.

### 2. Subir

```bash
cd novo/plataforma
bash deploy.sh          # responda "s" em "Subir o Dominica Learn?"
```

Num `.env` que já existe, o `deploy.sh` o mantém — para ligar depois, acrescente
as três linhas e rode de novo:

```
LEARN=on
VAULT_NO_HOST=/dados/vault
LEARN_ADMIN_EMAIL=voce@seudominio
```

À mão, sem o `deploy.sh`:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml \
               -f docker-compose.learn.yml up --build
```

As migrações do Learn rodam sozinhas na subida, como as da plataforma.

### 3. Primeira conta

O cadastro nasce **fechado**: só o admin cria contas. Para criar a dele, abra uma
vez pelo `.env` — nunca editando o compose, que é versionado:

```bash
echo 'LEARN_CADASTRO_ABERTO=true' >> .env && bash deploy.sh
```

Acesse `https://learn.SEU_DOMINIO/Account/Register` e crie a conta
com **o mesmo e-mail** que está em `LEARN_ADMIN_EMAIL` — é a igualdade desses dois
que faz de você o admin. Depois feche:

```bash
sed -i 's/^LEARN_CADASTRO_ABERTO=true/LEARN_CADASTRO_ABERTO=false/' .env && bash deploy.sh
```

Daí em diante, as contas dos outros saem da tela de administração.

**Não deixe aberto.** Enquanto estiver, qualquer pessoa que alcance a URL cria uma
conta, e cada conta nova é uma pasta nova de vault no seu disco.

---

## Quando não funciona: leia o SINTOMA

O endereço `learn.SEU_DOMINIO` pode responder três coisas bem diferentes, e
cada uma aponta para um lugar distinto. Confundi-las custa horas.

### "Caí na home da plataforma" (a tela de obra)

**O Caddy não tem regra para esse caminho.** Sem o bloco `handle`, tudo cai no
`reverse_proxy api:8080` do fim do arquivo — e o SPA da plataforma atende
*qualquer* caminho que não conheça, renderizando a home. Não dá 404, não dá erro:
dá a tela errada, com ar de que o deploy deu certo.

> **Use `./dc`, e não `docker compose`, para tudo abaixo.** O stack é montado com
> uma pilha de `-f` que varia com o `.env`. Um `docker compose ps learn` seco lê
> só o `docker-compose.yml`, não acha o serviço e responde *"no such service:
> learn"* — o que parece um diagnóstico ("o contêiner não existe") e não é: é a
> pergunta errada. O `deploy.sh` grava o `./dc` com a pilha certa a cada subida.

São **duas** causas possíveis, e elas dão a mesma tela. Confira as duas, nesta
ordem, no servidor:

```bash
cd novo/plataforma

# 1. o ARQUIVO tem a regra?
grep -c dominica-learn Caddyfile
#    0 → o código não chegou. git pull no ramo que a VPS acompanha.

# 2. o CADDY EM MEMÓRIA tem a regra?  (é outra pergunta — leia abaixo)
./dc exec caddy caddy fmt /etc/caddy/Caddyfile | grep -c dominica-learn
./dc logs caddy --tail=5
```

Um jeito rápido de ler a resposta do `curl -I`: **`last-modified` e `etag`
significam ARQUIVO ESTÁTICO** — é o `index.html` do SPA da plataforma, servido
pelo Kestrel da API. A tela de login do Learn é renderizada na hora e não tem
nenhum dos dois. Então `server: Kestrel` + `last-modified` = a requisição foi
para a `api`, não para o `learn`.

A segunda é a traiçoeira, e ela custou uma noite. **O `Caddyfile` entra por bind
mount de UM ARQUIVO**, e o Docker o amarra ao *inode* no momento em que o
contêiner nasce. Se depois disso o arquivo for **substituído** — e `git pull`,
`git stash` e `git checkout` substituem, não editam —, o contêiner continua
enxergando o arquivo **antigo para sempre**, enquanto não for recriado. E o
`up -d` não o recria, porque a definição do serviço não mudou.

O estrago é total e silencioso, e cada evidência isolada mente:

| você pergunta | responde | e ainda assim |
|---|---|---|
| `grep -c dominica-learn Caddyfile` (host) | `2` | o contêiner vê outro arquivo |
| `caddy reload` | sucesso | recarregou o arquivo VELHO |
| o app do Learn em 8080 | HTTP 200 | perfeito, e sem ninguém para encaminhar |

A pergunta que não mente é **dentro** do contêiner:

```bash
./dc exec caddy grep -c dominica-learn /etc/caddy/Caddyfile   # 0 = é isto
```

E a única cura é recriar:

```bash
./dc up -d --force-recreate caddy
```

O `deploy.sh` agora COMPARA o arquivo de dentro com o do disco antes de
recarregar, e recria o contêiner sozinho quando eles diferem.

E confirme que o contêiner do Learn existe:

```bash
./dc ps learn                # vazio = LEARN=off no .env, ou o overlay não entrou
./dc logs learn --tail=40    # subiu e caiu? o log diz por quê
```

### "502 Bad Gateway"

**Boa notícia: o Caddy tem a regra.** O que falta é o último salto até o
contêiner. Duas causas, nesta ordem de probabilidade:

**1. Permissão da pasta do vault.** O contêiner roda sem privilégio, com o uid
fixo **64198**. Se `VAULT_NO_HOST` pertence ao root — e pertence, porque foi o
root que a criou ou o próprio Docker ao montar —, o app não consegue escrever e
**morre no boot**. O contêiner pisca "Running", o proxy responde 502, e nada na
tela diz que o assunto é permissão. O `deploy.sh` agora ajusta isso sozinho; à
mão:

```bash
chown -R 64198:64198 "$(grep '^VAULT_NO_HOST=' .env | cut -d= -f2-)"
./dc up -d --force-recreate learn
```

**2. O app caiu por outro motivo** — banco, migração, e-mail do admin ausente.
O log diz qual:

```bash
./dc logs learn --tail=40
```

---

O caso geral: o Caddy **tem** a regra e o contêiner **não** está no ar — que é exatamente o
estado de quem atualizou o código mas não ligou o overlay. O `.env` manda:

```bash
grep -E '^(LEARN|VAULT_NO_HOST|LEARN_ADMIN_EMAIL)=' .env
```

`LEARN=off` (ou ausente) explica tudo. Ponha `LEARN=on`, preencha as outras duas e
rode `bash deploy.sh` de novo — **editar o `.env` sozinho não sobe contêiner
nenhum**, porque é o `deploy.sh` que monta a linha do compose com o overlay. Se
`LEARN=on` já estiver lá, o contêiner subiu e caiu: `./dc logs learn` diz por quê
(banco, vault, e-mail do admin).

### "A tela abre mas nada responde ao clique"

**Este é o pior de todos, porque não parece um defeito.** O Blazor Server entrega
a primeira tela pré-renderizada no servidor: ela aparece inteira, com barra,
menus e botões, mesmo que o circuito nunca conecte. O que falta é invisível.

Confira o arquivo do qual tudo depende:

```bash
curl -sI https://learn.SEU_DOMINIO/_framework/blazor.web.js | head -1
```

**404 aqui é a resposta.** Sem esse arquivo não há circuito e nenhum clique
funciona. Ele não existe por nenhum outro caminho — é o publish que tem de
colocá-lo em `wwwroot/_framework/`.

A causa já vista: **`--no-restore` no `dotnet publish`** do Dockerfile. O restore
do estágio anterior roda com só os `.csproj` presentes, e os assets estáticos do
framework se resolvem quando o projeto tem os arquivos dele; o `--no-restore`
proíbe o publish de refazer essa resolução. O resultado é um publish sem
`wwwroot/_framework` e um manifesto sem a rota — build verde, imagem menor, app
morto. Hoje o Dockerfile falha o build se o arquivo não sair, e o `deploy.sh`
confere a URL antes de dizer que terminou.

Se o `blazor.web.js` responder 200 e ainda assim nada funcionar, aí sim é o
WebSocket: veja *O que acontece quando a conexão cai*, e desconfie de qualquer
`timeouts` acrescentado ao bloco do Caddy.

---

## E-mail

O Learn manda três mensagens, todas com um link dentro: confirmar cadastro,
redefinir a frase secreta, e o código de redefinição. Nada mais.

**Ele usa a mesma caixa de saída da plataforma, com as mesmas variáveis do
`.env`.** Não há configuração nova a preencher: se `PLATAFORMA_SMTP_HOST` já
está lá para a Dominica, o Learn passa a mandar e-mail no próximo `deploy.sh`.
A tradução dos nomes acontece no `docker-compose.learn.yml`, não no código:

| `.env` (plataforma) | o Learn recebe como |
|---|---|
| `PLATAFORMA_SMTP_HOST` | `Email__Host` |
| `PLATAFORMA_SMTP_PORT` | `Email__Porta` |
| `PLATAFORMA_SMTP_USER` | `Email__Usuario` |
| `PLATAFORMA_SMTP_PASS` | `Email__Senha` |
| `PLATAFORMA_SMTP_FROM` | `Email__Remetente` |
| `PLATAFORMA_SMTP_SSL` | `Email__Ssl` |

### Sem servidor de e-mail o app continua inteiro — e diz que está sem

`Email__Host` vazio é o padrão, e não uma falha. Nesse estado:

* o Login **não** oferece "esqueci minha frase secreta", e diz para falar com
  quem administra;
* a tela de recuperação avisa que nenhuma mensagem vai chegar, em vez de mandar
  a pessoa esperar;
* quem se cadastra vê o link de confirmação **na própria tela**, e por isso não
  fica trancado do lado de fora.

Isso é decidido em um lugar só — `IEnviadorDeEmail.Ligado` — e as três telas
perguntam a ele. Nada disso é texto escrito à mão em cada página, justamente
para não haver uma tela que envelheça mentindo.

### Testando antes de confiar

Configurado o SMTP, o teste que vale é o ciclo inteiro, e leva um minuto:

1. `/Account/Login` → o link **"Esqueci minha frase secreta"** tem de aparecer.
   Se não apareceu, o app não enxergou a configuração — confira com
   `./dc exec learn printenv | grep Email__`.
2. Peça o link com o seu e-mail e confira a caixa de entrada (e o spam: o
   remetente é novo para o seu provedor).
3. Abra o link, troque a frase, entre com a nova.

No log do contêiner, um envio bem-sucedido aparece assim:

```
E-mail enviado para voce@exemplo.com: Redefinir sua frase secreta — Dominica Learn
```

O **assunto entra no log, o corpo não** — o corpo carrega o link de redefinir,
que é um token de acesso à conta.

### Os três erros que custam a tarde

**Porta 465.** Não use. Ela espera TLS implícito, e o `System.Net.Mail` conecta
em claro e negocia STARTTLS depois. O sintoma é um travamento sem mensagem útil,
e o palpite natural ("deve ser a senha") leva para o lado errado. Use **587**.

**Senha de login com 2FA ligada.** O servidor responde `authentication failed`
sem dizer por quê. Precisa ser a **senha de aplicativo**.

**Remetente diferente da caixa autenticada.** Ou o servidor recusa, ou aceita e
entrega direto no spam de quem recebe. `PLATAFORMA_SMTP_FROM` tem de ser a
própria caixa ou um alias dela.

Uma quarta, que não chega a custar a tarde porque o contêiner não sobe:
**`Email__Host` preenchido sem `Email__Remetente`** derruba a inicialização com
a mensagem dizendo exatamente isso. É de propósito — a metade-configuração
falharia só no primeiro envio, com alguém já esperando o e-mail.

---

## A mudança para o subdomínio

O Learn nasceu em `dominica.app.br/private/dominica-learn` e hoje mora em
**`learn.dominica.app.br`**. O código é o mesmo nos dois casos — foi por isso que
a troca não exigiu recompilar nada.

| | sub-caminho (como era) | subdomínio (como é) |
|---|---|---|
| `Hospedagem__CaminhoBase` | `/private/dominica-learn` | *(vazio)* — vem de `LEARN_CAMINHO_BASE` no `.env` |
| Caddy | `handle /private/dominica-learn*` proxeando | bloco de site `learn.{$DOMINICA_DOMAIN}`; o caminho antigo só **redireciona** |
| cookie | `Path` restrito ao caminho, nome próprio | nome próprio já basta |

### O que o operador precisa fazer

1. **DNS primeiro.** `learn.SEU_DOMINIO` tem de resolver para o IP desta máquina
   **antes** do deploy. O Caddy emite o certificado sozinho, mas só consegue
   depois que o nome resolve — sem isso o navegador nem chega ao servidor.
2. `LEARN_CAMINHO_BASE=` (vazio) no `.env`. É o padrão; só existe para o caminho
   de volta.
3. Deploy normal. O `handle_path` do caminho antigo passa a redirecionar.
4. **Confira que `https://learn.SEU_DOMINIO` abre e que um clique responde** (ver
   a seção sobre a conexão cair — página que renderiza não prova circuito vivo).
5. Só então troque o `302` do redirecionamento por `308` no `Caddyfile`.

### O que muda para quem já usava

- **Todo mundo é deslogado uma vez.** Cookie é por origem: o de
  `dominica.app.br` não é enviado para `learn.dominica.app.br`. Não há perda de
  dado — o vault é arquivo no disco do servidor —, é só entrar de novo.
- **Quem instalou o PWA precisa reinstalar.** O service worker e o `start_url`
  são presos à origem antiga; a instalação velha continua apontando para lá e vai
  cair no redirecionamento, saindo da janela do app. Desinstale e instale de novo
  pelo endereço novo.
- **O tema salvo volta ao padrão** uma vez, pelo mesmo motivo do cookie:
  `localStorage` é por origem.
- **Links antigos continuam funcionando**, inclusive com nome de nota acentuado e
  com a query dos e-mails de redefinição de senha — conferido rodando o Caddy
  contra a linha que está no repositório:

  | pedido no endereço antigo | para onde vai |
  |---|---|
  | `/private/dominica-learn` | `https://learn.…/` |
  | `/private/dominica-learn/notas/Direito/Aula%201` | `https://learn.…/notas/Direito/Aula%201` |
  | `/private/dominica-learn/Account/ResetPassword?code=abc&returnUrl=%2Fnotas` | mesma query, intacta |
  | `/inicio` (a plataforma) | não é tocado |

  Enquanto for `302`, um **POST** de aba velha vira GET no caminho — é o preço de
  o redirecionamento ser reversível. Com `308` ele continua POST.

### Por que 302 e não 301

301 e 308 ficam no cache do navegador por tempo indeterminado. Se o subdomínio
subisse com DNS errado ou sem certificado, quem visitasse o endereço antigo uma
única vez ficaria preso — e reverter o `Caddyfile` não soltaria, porque o
navegador nem chega a perguntar de novo. O `302` custa um pouco de latência e
compra a possibilidade de voltar atrás. Promova depois de confirmar.

---

## As armadilhas do sub-caminho, e como elas foram fechadas

Esta seção descreve o modo **sub-caminho**, que hoje é o caminho de volta e não o
normal. Ela fica porque as quatro armadilhas voltam no instante em que alguém
preencher `LEARN_CAMINHO_BASE` — e porque a 3ª (nenhum link com barra inicial)
vale nos dois modos.

Servir um app .NET sob um caminho é onde este deploy tem risco de verdade.
Quatro coisas quebram, e as quatro estão resolvidas — mas se alguém mexer no
código sem saber disso, elas voltam:

1. **`UsePathBase`** faz o roteamento enxergar `/notas` quando a URL é
   `/private/dominica-learn/notas`. As rotas das páginas seguem escritas sem o
   prefixo.

2. **O pipeline inteiro é declarado explicitamente** em `Program.cs` —
   `UseRouting`, `UseAuthentication`, `UseAuthorization` na ordem. O
   `WebApplication` os insere sozinho quando não são declarados, e a posição que
   ele escolhe deixa de servir com `UsePathBase` em jogo. Isso deu dois defeitos
   em sequência, ambos só visíveis rodando: primeiro o GET funcionava e o POST do
   formulário de login dava **405**; depois, com só o roteamento explícito, toda
   página com `[Authorize]` quebrava com *"no middleware that supports
   authorization"*.

3. **Nenhum link do app começa com barra.** `href="/grafo"` é relativo à raiz do
   **documento** e ignora o `<base href>` — sairia do sub-caminho e daria 404.
   Todos os links são relativos (`grafo`, `notas/…`), inclusive a URL das imagens
   de anexo. Se você acrescentar um link novo, escreva sem a barra inicial.

   **`NavigationManager.NavigateTo` NÃO é exceção — este documento afirmava que
   era, e estava errado.** O Blazor resolve com as regras de URI: uma relativa que
   começa com barra é resolvida contra a raiz do domínio, e o sub-caminho some.
   `NavigateTo("/notas/x")` sob `/private/dominica-learn` vai para
   `dominio.com/notas/x` e dá 404. Aconteceu em produção, logo depois de criar uma
   nota — o usuário criava e era jogado numa página de erro.

   Em desenvolvimento o defeito é invisível, porque sem sub-caminho as duas formas
   dão no mesmo. Por isso as URLs internas agora saem de `Rotas`, com testes que
   falham se alguma voltar a nascer com barra.

4. **O cookie tem nome e `Path` próprios.** No mesmo domínio, dois apps com
   cookie de mesmo nome se derrubam, e o sintoma — *"fui deslogado sozinho"* —
   ninguém liga à causa.

Uma observação honesta: `UsePathBase` **não impede** o app de responder também na
raiz (`/notas` continua atendendo). Atrás do Caddy isso não aparece, porque só o
que casa `/private/dominica-learn*` chega ao Learn. Mas não exponha a porta do
contêiner direto.

---

## O que acontece quando a conexão cai

Isto merece seção própria porque é a diferença entre "a tela abriu" e "o app
funciona", e as duas coisas são independentes num Blazor Server servido sob um
caminho: a **primeira** resposta é HTML pré-renderizado no servidor e aparece
inteira mesmo que o WebSocket nunca conecte. Se o `_blazor` não subir, o usuário
vê a página montada, com todos os botões, e **nada responde ao clique** — sem
erro, sem console, sem pista. Por isso o que vale conferir num deploy novo não é
se a página renderiza, é se um clique faz alguma coisa.

Foi conferido rodando, com o app sob `/private/dominica-learn`:

| | resultado |
|---|---|
| WebSocket do circuito | abre em `…/private/dominica-learn/_blazor` |
| clique de verdade (abrir um diálogo) | funciona — o circuito está vivo, não é só pré-render |
| rede cai | o aviso de reconexão aparece sozinho e **bloqueia a tela** |
| enquanto está fora do ar | a tela fica inerte; clique nenhum faz efeito |
| rede volta | reconecta sozinho, sem recarregar |
| depois de reconectar | a interatividade volta, e o texto digitado no editor continua lá |

**Não existe modo offline, e isso é preço declarado do Blazor Server** (ver
`ARQUITETURA.md`). O app exige conexão viva. O que existe é reconexão: o circuito
fica guardado no servidor por alguns minutos, e enquanto ele durar a volta é
transparente — o estado da tela, inclusive o texto do editor, sobrevive.

O que se perde numa queda é limitado por construção: o editor grava sozinho após
1,2 s parado (`wwwroot/js/editor.js`), então o pior caso é a última frase
digitada. Se a máquina dormir tempo demais e o circuito expirar, o aviso passa a
"Não consegui reconectar" e a página recarrega — a nota volta do disco, na versão
do último autosave.

**O que isso obriga do proxy:** o Caddy tem de repassar o `Upgrade` do WebSocket
e **não** pode ter um timeout de leitura curto no caminho do Learn. O
`reverse_proxy` padrão do Caddy já faz as duas coisas — a armadilha é acrescentar
um `timeouts`/`flush_interval` "de segurança" nesse bloco depois. Um timeout ali
derruba o circuito de tempos em tempos, e o sintoma é o aviso de reconexão
piscando na tela de quem está estudando.

---

## Backup — o que precisa ser salvo

Em ordem de importância:

1. **O vault** (`VAULT_NO_HOST`). São os arquivos `.md`: as notas, os anexos e o
   agendamento dos flashcards. É o único lugar onde o conhecimento existe.
2. **`learn_registro`**: as **horas estudadas** e as **questões resolvidas** por
   matéria. É o segundo da lista, e não o último, porque é o único item aqui que
   **ninguém consegue refazer**: o vault se copia, as contas se recadastram, o
   índice se reconstrói — mas quantas horas você estudou tributário em março só
   existe porque você registrou em março.
3. **`learn_identidade`**: as contas. Sem ele ninguém entra, mas nada se perde —
   dá para recadastrar e reapontar os apelidos para as pastas existentes.
4. **`learn_indice`**: **descartável por construção**. Ele é derivado do vault;
   apagar e deixar reconstruir na próxima subida é operação de rotina. A única
   coisa que só existe nele é o histórico de revisões das notas — se isso
   importar para você, entra no backup; se não, ignore.

No stack da plataforma isso já está automatizado: o serviço `backup` faz `pg_dump`
diário de `dominica`, `learn_identidade` e `learn_registro`, e **pula o
`learn_indice` de propósito**. Ver `novo/plataforma/DEPLOY.md` → *Backup
automático do Postgres*. O **vault** não passa por ali — ele é disco do host.
