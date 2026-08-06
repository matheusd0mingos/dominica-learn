# Deploy do Dominica Learn

## A pergunta primeiro: é o mesmo arquivo de deploy da plataforma?

**Agora é — o `novo/plataforma/deploy.sh` sobe os dois.**

Era preciso que fosse. Servir o Learn em `/private/dominica-learn` põe ele no
domínio da plataforma, então quem encaminha a requisição é o **Caddy da
plataforma** — e o Caddy só alcança o contêiner do Learn se os dois estiverem na
mesma rede do Compose. Não havia como manter dois stacks separados e o caminho ao
mesmo tempo.

Juntar trouxe duas vantagens além dessa obrigação, e as duas contam num VPS pequeno:

1. **Um Postgres em vez de dois.** São ~200 MB de RAM e um alvo de backup a menos.
   Os dois bancos do Learn (`learn_indice`, `learn_identidade`) seguem separados —
   compartilhar o **servidor** sem compartilhar o **esquema** é exatamente o que o
   próprio código já dizia fazer.
2. **Um `deploy.sh` só.** Dois roteiros de deploy é um roteiro que alguém esquece
   de rodar.

O compose próprio do Learn (`novo/learn/docker-compose.yml`) continua existindo e
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
| `novo/plataforma/Caddyfile` | o `handle /private/dominica-learn*` |
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
   cria os dois bancos e sai (o mesmo padrão do serviço `migrate` da plataforma).

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

Acesse `https://SEU_DOMINIO/private/dominica-learn/Account/Register` e crie a conta
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

O caminho `/private/dominica-learn` pode responder três coisas bem diferentes, e
cada uma aponta para um lugar distinto. Confundi-las custa horas.

### "Caí na home da plataforma" (a tela de obra)

**O Caddy não tem regra para esse caminho.** Sem o bloco `handle`, tudo cai no
`reverse_proxy api:8080` do fim do arquivo — e o SPA da plataforma atende
*qualquer* caminho que não conheça, renderizando a home. Não dá 404, não dá erro:
dá a tela errada, com ar de que o deploy deu certo.

Quase sempre a causa é que **o código do Learn não está no servidor**: o
`Caddyfile` do servidor é o do `git pull`, e se o commit que acrescenta o `handle`
não está no ramo que a VPS acompanha, ele não existe lá. Confira no servidor:

```bash
cd novo/plataforma
grep -c dominica-learn Caddyfile      # 0 = o código não chegou; faça o pull/merge
docker compose ps learn               # vazio = o contêiner nunca subiu
```

### "502 Bad Gateway"

O Caddy **tem** a regra e o contêiner **não** está no ar — que é exatamente o
estado de quem atualizou o código mas não ligou o overlay. O `.env` manda:

```bash
grep -E '^(LEARN|VAULT_NO_HOST|LEARN_ADMIN_EMAIL)=' .env
```

`LEARN=off` (ou ausente) explica tudo. Ponha `LEARN=on`, preencha as outras duas e
rode `bash deploy.sh` de novo. Se `LEARN=on` já estiver lá, o contêiner subiu e
caiu — `docker compose logs learn` diz por quê (banco, vault, e-mail do admin).

### "A tela abre mas nada responde ao clique"

O WebSocket do circuito não subiu. Ver a seção *O que acontece quando a conexão
cai* mais adiante — e desconfie de qualquer `timeouts` acrescentado ao bloco do
Caddy.

---

## O que muda entre sub-caminho e subdomínio

Só uma variável: `Hospedagem__CaminhoBase`.

| | sub-caminho | subdomínio |
|---|---|---|
| `Hospedagem__CaminhoBase` | `/private/dominica-learn` | *(vazio)* |
| Caddy | `handle /private/dominica-learn*` no site da plataforma | um bloco de site próprio para `learn.dominica.app.br` |
| cookie | `Path` restrito ao caminho, nome próprio | nome próprio já basta |

O código é o mesmo nos dois casos, e é por isso que a escolha pode mudar depois
sem recompilar nada.

---

## As armadilhas do sub-caminho, e como elas foram fechadas

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
   (`NavigationManager.NavigateTo("/notas")` é exceção: o Blazor já resolve
   aquilo contra a base.)

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
2. **`learn_identidade`**: as contas. Sem ele ninguém entra, mas nada se perde —
   dá para recadastrar e reapontar os apelidos para as pastas existentes.
3. **`learn_indice`**: **descartável por construção**. Ele é derivado do vault;
   apagar e deixar reconstruir na próxima subida é operação de rotina. A única
   coisa que só existe nele é o histórico de revisões das notas — se isso
   importar para você, entra no backup; se não, ignore.
