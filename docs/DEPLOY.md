# Deploy do Dominica Learn

## A pergunta primeiro: é o mesmo arquivo de deploy da plataforma?

**Hoje não, e passar a ser é o certo.**

Hoje são dois deploys independentes:

| | Plataforma | Learn |
|---|---|---|
| compose | `novo/plataforma/docker-compose.yml` + `.prod.yml` | `novo/learn/docker-compose.yml` |
| banco | um Postgres (`db`) | **outro** Postgres (`banco`) |
| proxy | Caddy, com TLS | nenhum |
| subir | `deploy.sh` | manual |

Isso funciona enquanto o Learn mora num domínio próprio. **Deixa de funcionar no
instante em que ele passa a viver em `/private/dominica-learn`**, e a razão é
simples: esse caminho está no domínio da plataforma, então quem tem de encaminhar
a requisição é o **Caddy da plataforma** — e o Caddy só alcança o contêiner do
Learn se os dois estiverem na mesma rede Docker.

Juntar tem duas vantagens além dessa obrigação, e as duas contam num VPS pequeno:

1. **Um Postgres em vez de dois.** São ~200 MB de RAM e um alvo de backup a menos.
   O Learn já usa dois bancos separados (`learn_indice`, `learn_identidade`) e o
   `docker/criar-bancos.sh` existe para criá-los — compartilhar o **servidor** sem
   compartilhar o **esquema** é exatamente o que o próprio código já diz que faz.
2. **Um `deploy.sh` só.** Dois roteiros de deploy é um roteiro que alguém esquece
   de rodar.

---

## Passo a passo — pondo o Learn na plataforma

### 1. O Learn vira um serviço do compose da plataforma

Em `novo/plataforma/docker-compose.prod.yml`, ao lado de `api` e `caddy`:

```yaml
  learn:
    image: dominica-learn:local
    build:
      context: ../..                 # o repo inteiro, como o da api
      dockerfile: novo/learn/Dockerfile
    restart: unless-stopped
    depends_on:
      db:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      # MESMO servidor Postgres da plataforma, bancos SEPARADOS.
      ConnectionStrings__Indice: "Host=db;Database=learn_indice;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      ConnectionStrings__Identidade: "Host=db;Database=learn_identidade;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      Vault__Raiz: /dados/vault
      # O caminho onde o Learn é servido. Vazio = raiz do domínio (subdomínio).
      Hospedagem__CaminhoBase: /private/dominica-learn
      # O admin mestre: a MESMA pessoa que administra a Dominica, com conta daqui.
      Administracao__EmailDoAdminMestre: ${LEARN_ADMIN_EMAIL:?defina no .env}
      # Fechado: só o admin cria contas. Ver OpcoesDeAdministracao.
      Administracao__CadastroAberto: "false"
    volumes:
      - ${VAULT_NO_HOST:?defina VAULT_NO_HOST no .env}:/dados/vault
```

Os dois bancos precisam existir. O `novo/learn/docker/criar-bancos.sh` faz isso —
monte-o no `db` da plataforma como já é feito no compose do Learn.

### 2. O Caddy encaminha o caminho

Em `novo/plataforma/Caddyfile`, **antes** do `reverse_proxy api:8080` final:

```caddy
	# O Learn vive sob este caminho. handle, e NÃO handle_path: o prefixo tem de
	# CHEGAR ao Learn, porque é ele quem o remove (UsePathBase) e é ele quem o
	# escreve de volta no <base href> das páginas. Com handle_path o prefixo seria
	# cortado aqui e todo link do app apontaria para a raiz do domínio.
	handle /private/dominica-learn* {
		reverse_proxy learn:8080
	}
```

O WebSocket do Blazor Server desce por esse mesmo caminho (`/private/dominica-learn/_blazor`),
então não precisa de regra própria — o Caddy repassa `Upgrade` sozinho.

### 3. Subir

```bash
cd novo/plataforma
docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build
```

As migrações do Learn rodam sozinhas na subida, como as da plataforma.

### 4. Primeira conta

O cadastro nasce **fechado**. Para criar a sua:

1. suba com `Administracao__CadastroAberto: "true"` **uma vez**;
2. acesse `https://SEU_DOMINIO/private/dominica-learn/Account/Register` e crie a
   conta com o e-mail que está em `LEARN_ADMIN_EMAIL`;
3. volte a `"false"` e suba de novo.

Daí em diante, você cria as contas dos outros pela tela de administração.

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
