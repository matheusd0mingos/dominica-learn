# O norte — de vault de notas a sistema de estudos

Este documento responde a uma pergunta que o `ARQUITETURA.md` não responde: **para onde isto vai.**

Ele parte do que já existe e funciona, não de uma folha em branco — porque já existe muito, e ignorar
isso produziria um desenho bonito e inútil.

---

## O que já está de pé

Está construído, testado e verificado no navegador:

| Pilar | Estado |
|---|---|
| Notas em `.md`, wikilinks, backlinks, etiquetas hierárquicas, anexos | pronto |
| Grafo do vault, calculado em C#, determinístico | pronto |
| Flashcards SM-2 gravados no próprio `.md`, no formato do Obsidian | pronto |
| Fila de revisão intercalada, teto diário de inéditos, suspender | pronto |
| Lacuna (`==destaque==`), cartão a partir de seleção, erro → cartão | pronto |
| Busca por nome com pontuação, abridor rápido, autocompletar `[[` | pronto |
| Exportar e importar o vault, com defesas de zip slip e bomba | pronto |
| Isolamento por usuário, provado com testes e duas contas no navegador | pronto |
| Compartilhar uma matéria — desenho e decisão de acesso | núcleo pronto |

**O que falta não é a biblioteca. É a espinha que transforma uma biblioteca em sistema de estudos:**
o **edital** e o **registro de desempenho**. São esses dois que respondem "o que estudar hoje" e "onde
estou perdendo desempenho" — as duas perguntas que o produto promete e hoje não responde.

---

## A regra fundadora, refinada

A regra atual é:

> O disco é a fonte da verdade. O índice é derivado e descartável.
> Nada que exista só no índice pode ser conhecimento do usuário.

Ela funcionou porque só havia dois tipos de dado. Com edital e desempenho, aparece um terceiro, e a regra
precisa de um degrau a mais. **São três categorias, com três casas diferentes:**

### 1. Conhecimento → arquivo `.md` no vault

Notas, cartões, agendamento de revisão, etiquetas, o edital (ver adiante).

Sobrevive a apagar o banco. Abre no Obsidian. Sai inteiro no `.zip`.

### 2. Registro → Postgres, **durável e não descartável**

Sessões de estudo, questões resolvidas, simulados.

**Não é conhecimento** — é histórico factual. Não faz sentido em Markdown (ninguém lê "resolvi 40
questões às 14h32" numa nota), e não é reconstruível a partir de nada: uma série temporal perdida está
perdida.

Isso obriga a uma consequência que precisa estar escrita: **o backup passa a ter de levá-lo junto.**
Hoje o `.zip` exporta só os arquivos. No dia em que existir registro de desempenho, o pacote precisa
incluí-lo — em CSV ou JSON, dentro do zip — ou a promessa "o vault é seu" vira meia verdade.

### 3. Derivado → Postgres, descartável

Índice de busca, grafo, estatísticas, cobertura do edital, estado dos nós do mapa.

Reconstruível a partir de (1) e (2). Apagar e reindexar continua sendo rotina, não desastre.

> **O teste para saber onde um dado mora:** se perdê-lo custa *reaprender*, é (1). Se custa *não saber
> mais o que aconteceu*, é (2). Se custa *esperar um reindex*, é (3).

---

## A ideia central: o edital é um conjunto de notas

Este é o ponto de maior alavancagem do desenho inteiro, e ele reaproveita tudo que já existe.

O problema óbvio: o tópico do edital ("ICMS") e a nota de conteúdo (`Direito tributário/ICMS.md`) são
coisas diferentes. Como ligá-las?

- **Marcar cada nota com o tópico** — trabalho manual em cada nota, e trabalho manual não é feito.
- **Adivinhar por semelhança de nome** — erra, e uma cobertura de edital que erra ninguém consulta duas
  vezes.
- **O tópico do edital É uma nota, e a ligação é um `[[wikilink]]`.** ← esta.

Importar um edital cria uma pasta:

```
Editais/
  AFRFB 2026/
    AFRFB 2026.md                      ← o mapa, com os links para os tópicos
    Direito Tributário — Competência.md
    Direito Tributário — ICMS.md
    Direito Tributário — ISS.md
    …
```

Cada nota de tópico é curta e nasce quase vazia. Ela ganha valor quando você a liga ao que escreveu:
`[[Direito tributário/ICMS]]`. E **as menções não ligadas, que já existem, fazem esse trabalho quase
sozinhas** — a nota de tópico "ICMS" mostra todas as suas notas que dizem "ICMS" e oferece ligar.

O que isso compra, de graça:

- **cobertura do edital** = quantos tópicos têm ao menos uma ligação. Sai do grafo que já existe;
- **o mapa de conhecimento** = o grafo filtrado pela pasta do edital. Já está construído;
- **a busca, as etiquetas, o backup, o compartilhamento** funcionam no edital sem uma linha nova;
- **o edital é do usuário**: mora em arquivos, abre no Obsidian, sai no zip. A regra fundadora vale sem
  exceção.

O custo, dito às claras: importar um edital em PDF e transformá-lo nessa árvore é trabalho de verdade, e
é aí que a IA entra com o melhor retorno do sistema todo — não gerando resumo, mas **transformando um
edital colado em texto numa árvore de tópicos que a pessoa revisa e aceita**.

---

## Os estados do mapa são DERIVADOS, nunca marcados à mão

O pedido é "cada nó pode ser: não estudado, estudado, revisado, dominado, baixo desempenho".

Se esses estados forem um campo que a pessoa preenche, eles ficam desatualizados na segunda semana e o
mapa passa a mentir — e um mapa que mente é pior que nenhum, porque decisões são tomadas a partir dele.

Todos saem de sinais que o sistema já tem ou vai ter:

| Estado | De onde sai |
|---|---|
| **Não estudado** | tópico do edital sem nenhuma nota ligada |
| **Estudado** | tem nota ligada, sem cartões |
| **Em revisão** | tem cartões com agendamento vivo |
| **Dominado** | cartões com intervalo acima de N dias **e** acerto em questões acima de X% |
| **Baixo desempenho** | acerto abaixo de X%, ou facilidade dos cartões caindo |

**"Dominado" exige os dois sinais, e isso não é rigor gratuito:** cartão mede memória, questão mede
aplicação. Um sozinho mente. Quem acerta todos os cartões de ICMS e erra 60% das questões de ICMS não
domina ICMS — sabe recitar. É exatamente o autoengano que o produto existe para desfazer.

---

## Os contextos

Sete, e a direção das dependências importa mais que a lista.

```
┌─────────────────┐     ┌──────────────────┐
│  CONHECIMENTO   │     │    PROGRAMA      │
│  notas, links,  │◄────┤  edital, tópicos │
│  etiquetas,     │     │  cobertura       │
│  anexos, grafo  │     └────────┬─────────┘
└────────┬────────┘              │
         │                       │
         ▼                       │
┌─────────────────┐              │
│    MEMÓRIA      │              │
│  cartões, SM-2, │              │
│  fila, teto     │              │
└────────┬────────┘              │
         │        ┌──────────────┴───┐
         │        │   DESEMPENHO     │
         │        │  sessões,        │
         │        │  questões,       │
         │        │  simulados       │
         │        └──────────┬───────┘
         │                   │
         └────────┬──────────┘
                  ▼
         ┌──────────────────┐      ┌──────────────────┐
         │      PLANO       │      │   ASSISTENTE     │
         │  o que agora,    │      │  porta de IA     │
         │  prioridade      │      │  (sem provedor)  │
         └──────────────────┘      └──────────────────┘

         ┌──────────────────────────────────────────┐
         │  IDENTIDADE E ACESSO                     │
         │  contas, apelido, concessões             │
         └──────────────────────────────────────────┘
```

**Nada depende do Plano.** Isso é deliberado: o primeiro algoritmo de priorização vai estar errado, e o
segundo também. Ele precisa ser descartável sem arrastar nada junto.

**O Assistente não é chamado por ninguém — ele chama.** A IA propõe (tópicos de um edital, cartões de uma
nota, um ajuste no plano) e a pessoa aceita, edita ou descarta. Nada entra no vault sem passar por um
humano. Isso não é cautela: formular a pergunta *é* o estudo, e um sistema que a formula por você
entrega cartões que ninguém aprende.

---

## Entidades e agregados

Só o que é novo. O que existe está no `ARQUITETURA.md`.

### Programa

```
Edital                          ← agregado
  Id, Concurso, Cargo, Banca, DataDaProva, PastaNoVault
  Topicos: TopicoDoEdital[]

TopicoDoEdital                  ← entidade dentro do agregado
  Id, Disciplina, Titulo, Ordem, Pai (auto-referência), CaminhoDaNota
```

**Invariante do agregado:** um tópico não existe fora de um edital, e a árvore não tem ciclo.

`CaminhoDaNota` é a única ponte para o Conhecimento — e é um `CaminhoNota`, não um Id. Isso mantém os
dois contextos desacoplados: o Programa não sabe o que é uma nota, sabe onde ela mora.

### Desempenho

```
SessaoDeEstudo                  ← agregado
  Id, Usuario, Inicio, Fim, Disciplina, TopicoId?, Origem (manual | revisão | simulado)

LoteDeQuestoes                  ← agregado
  Id, Usuario, Em, TopicoId?, Disciplina, Total, Acertos, TempoTotal, Fonte (texto livre)

Simulado                        ← agregado
  Id, Usuario, Em, Nome, Lotes: LoteDeQuestoes[]
```

**Lote, e não questão.** Registrar questão a questão é o desenho que parece mais completo e é o que
ninguém preenche: quem acabou de fazer 40 questões quer digitar "40, acertei 27", não abrir 40
formulários. O ganho de granularidade seria real e o dado não existiria.

### Plano

```
BlocoDeHoje                     ← objeto de valor, NÃO persistido
  Topico, Motivo, MinutosSugeridos, Tipo (revisar | estudar | questões)
```

**Não é persistido de propósito.** Ver a seção do cronograma.

---

## Casos de uso

Os que faltam, agrupados por contexto. Cada um resolve uma pergunta que o usuário faz em voz alta.

**Programa**
- `ImportarEdital(texto)` → cria a pasta e as notas de tópico *(IA propõe a árvore; a pessoa aceita)*
- `CoberturaDoEdital(edital)` → tópicos com nota / total, por disciplina
- `MapaDoEdital(edital)` → o grafo com o estado derivado de cada nó
- `LigarTopicoANota(topico, caminho)` — e o inverso

**Desempenho**
- `RegistrarQuestoes(topico, total, acertos, tempo, fonte)`
- `RegistrarSessao(inicio, fim, disciplina)` — e a versão automática, a partir da revisão
- `RegistrarSimulado(nome, lotes)`
- `DesempenhoPorDisciplina(periodo)`

**Plano**
- `OQueAgora()` → 3 a 5 blocos, cada um com o motivo escrito
- `PrevisaoDeConclusao(edital, horasPorSemana)` → data, com a margem de erro

**Assistente**
- `ProporTopicos(textoDoEdital)`
- `ProporCartoes(nota)` — propõe, nunca grava
- `ExplicarDiferenca(topicoA, topicoB)` — a pergunta que mais se faz num vault de concurso

---

## O cronograma: não montar um calendário

O pedido é "o sistema monta automaticamente um cronograma". É a armadilha mais cara deste projeto.

Um calendário de seis meses fica vermelho na primeira semana em que a vida acontece — e um plano
permanentemente vermelho não é corrigido, é abandonado, e leva o app junto. Todo concurseiro já tem uma
planilha assim, morta, em algum lugar.

**O que serve é responder "o que agora".** Três a cinco blocos, recalculados todo dia, com o motivo de
cada um escrito na tela:

> **ICMS — 25 min**
> 14 cartões vencidos e 41% de acerto nas últimas 60 questões. É onde você mais perde ponto por hora.

> **Controle de constitucionalidade — 40 min**
> Nunca estudado, e são 8% do edital.

Isso é mais simples de construir, muito mais útil, e não cria dívida. O calendário completo continua
existindo — mas como **projeção** ("neste ritmo, você termina o edital em 14 de março"), não como
compromisso que se quebra.

A previsão precisa vir com a margem: *"em 14 de março, se mantiver 12h/semana. Nas últimas 4 semanas
você fez 9h."* Uma previsão sem o confronto com o realizado é adivinhação com cara de dado.

---

## Estatísticas: cinco, não onze

Cada indicador a mais dilui a atenção do que importa. O critério para um indicador existir é duro:
**ele muda o que você faz amanhã?**

Ficam:

1. **Cobertura do edital** — % de tópicos com nota, por disciplina. Responde "quanto falta".
2. **Acerto por disciplina, últimas N questões** — responde "onde estou perdendo".
3. **Revisões vencidas por disciplina** — responde "o que revisar".
4. **Planejado × realizado (horas/semana)** — responde "meu plano é real?".
5. **Mapa de calor semanal** — responde "quando eu de fato estudo?", que é o que permite planejar.

Saem, e o motivo:

- **Sequência de dias consecutivos (streak).** Numa preparação de dois anos, o streak transforma um dia
  de descanso em fracasso e produz a culpa que faz a pessoa fechar o app e não voltar. É desenhado para
  produto de engajamento diário, não para maratona. **Cortar é uma decisão de produto, não de escopo.**
- **Retorno por hora estudada.** Soa científico e não é acionável: você não muda nada com esse número.
- **Tempo médio por questão.** Só importa se a prova aperta o tempo — deixar para quando houver
  simulado cronometrado.
- **Eficiência por disciplina.** É o (2) com outro nome.

---

## As três tensões técnicas que precisam de decisão

### 1. Offline-first é incompatível com Blazor Server

Não é dificuldade, é impossibilidade: o Blazor Server exige um WebSocket vivo — sem rede, não há
aplicação, há uma tela congelada.

Offline de verdade exige **Blazor WebAssembly** (ou app nativo) + armazenamento local + sincronização com
resolução de conflito. Isso é um produto inteiro — é literalmente o que a Obsidian cobra US$ 4/mês para
fazer — e refaria a arquitetura de apresentação do zero.

**Proposta:** separar as duas coisas que "PWA" costuma significar.

- **PWA instalável — sim, e barato.** Manifesto, ícone, tela cheia, splash. O app ganha ícone na tela
  inicial e abre sem barra de navegador. Resolve a maior parte do que se quer de "app no celular" e
  funciona com Blazor Server sem mudar nada.
- **Offline de verdade — não agora**, e com o custo dito: é a decisão de trocar Server por WASM. Se um
  dia for prioridade, o lugar de começar é a **revisão** — é a tela que faz sentido no ônibus, e é a
  única que caberia num modo offline pequeno e autocontido.

### 2. JWT hoje seria uma piora

O pedido de "autenticação JWT" faz sentido quando há cliente separado — app nativo, API pública, SPA de
terceiro. Não é o caso.

Num navegador com Blazor Server, JWT significa guardar o token em JavaScript, ao alcance de qualquer XSS.
O cookie atual é `HttpOnly` + `SameSite=Strict` + `Secure`: fora do alcance de script, e não viaja em
requisição de terceiro. Trocar seria abrir uma porta para fechar nenhuma.

**Proposta:** manter cookie. A arquitetura já isola isso atrás de `IUsuarioAtual` — no dia em que houver
app nativo, entra um segundo adaptador ao lado, e nada do domínio muda.

### 3. Um projeto por contexto seria cerimônia sem ganho

O reflexo em Clean Architecture é criar `Learn.Programa.Domain`, `Learn.Desempenho.Domain`, e assim por
diante. Para um sistema mantido por uma pessoa, isso compra zero e custa muito: sete vezes mais arquivos
de projeto, referências cruzadas para gerenciar, e tempo de build multiplicado.

**Proposta:** manter os quatro projetos e separar contexto por **namespace**:

```
Dominica.Learn.Domain/
  Vault/  Analise/  Ligacoes/  Cartoes/  Grafo/     ← existem
  Programa/                                          ← novo
  Desempenho/                                        ← novo
  Plano/                                             ← novo
```

A fronteira que importa é a de dependência, e ela se garante com disciplina e revisão — não com arquivo
`.csproj`. Dividir em projetos separados só quando um contexto precisar de **deploy separado**, e nenhum
vai precisar.

---

## Banco de dados

Só o que é novo. Dois esquemas, com garantias diferentes.

### `registro` — durável, faz parte do backup

```sql
sessoes_de_estudo (id, usuario, inicio, fim, disciplina, topico_id, origem)
lotes_de_questoes (id, usuario, em, topico_id, disciplina, total, acertos, segundos, fonte)
simulados        (id, usuario, em, nome)
```

Índice por `(usuario, em)` em tudo: toda consulta é "meu desempenho no período".

### `derivado` — descartável, reconstruível

```sql
editais          (id, usuario, concurso, cargo, banca, data_da_prova, pasta)
topicos          (id, edital_id, usuario, disciplina, titulo, ordem, pai_id, caminho_da_nota)
cobertura_cache  (topico_id, estado, atualizado_em)
```

**`editais` e `topicos` são derivados** — reconstruídos lendo a pasta `Editais/` do vault, que é a fonte.
Isso mantém a regra fundadora intacta: apagar o banco e reindexar recria o edital inteiro.

O filtro global por usuário do EF Core, que já protege as tabelas existentes, se aplica a todas estas.

---

## Padrões — e os que não valem a pena

**Vale a pena, e já está em uso:**

- **Portas e adaptadores** para tudo que é infraestrutura. É o que vai permitir trocar de provedor de IA
  sem tocar em caso de uso nenhum.
- **Objeto de valor com fábrica que falha** (`CaminhoNota.TentarCriar`) — impede estado inválido de
  existir, em vez de validá-lo em toda camada.
- **Função pura no domínio** para tudo que é regra. É o que faz o teste custar três linhas, e teste que
  custa três linhas é o teste que ainda existe daqui a cinco anos.
- **Resultado em vez de exceção** para fluxo esperado.

**Entra:**

- **Especificação** para as regras de priorização do Plano — elas vão mudar toda semana no começo, e
  precisam ser combináveis e testáveis isoladamente.
- **Serviço de domínio** para o estado do nó do mapa: ele cruza três contextos e não pertence a nenhum.

**Não vale a pena aqui:**

- **CQRS com barramento e handlers.** Ganho real só aparece com times separados ou leitura/escrita em
  escalas diferentes. Aqui adicionaria uma camada de indireção entre a tela e a resposta, e a resposta
  já é rápida.
- **Event sourcing.** Sedutor para "histórico de estudo" e caro demais: o registro de desempenho é
  append-only por natureza, o que já dá 90% do benefício sem nenhum do custo.
- **Repositório genérico.** Esconde a consulta que importa e produz `IQueryable` vazando por toda parte.

---

## Roadmap — do que existe até a 1.0

Cada fase termina em algo utilizável. Nenhuma fase entrega "infraestrutura para depois".

### Fase 1 — Fechar o ciclo que já existe *(quase pronto)*

Falta: compartilhar matéria (tela e persistência), PWA instalável, backup levando o registro.

**Termina quando:** dá para estudar sozinho, no celular, por três meses, sem faltar nada.

### Fase 2 — O edital

Importar edital (colar texto → árvore de tópicos, revisada à mão), notas de tópico, cobertura por
disciplina, mapa do edital com os estados derivados.

**Termina quando:** o app responde "quanto falta para concluir o edital" com um número em que se confia.

### Fase 3 — O desempenho

Registro de lotes de questões e de sessões. Os cinco indicadores. Acerto por disciplina alimentando o
estado "baixo desempenho" no mapa.

**Termina quando:** o app responde "onde estou perdendo desempenho" apontando disciplina e tópico.

### Fase 4 — O "o que agora"

Priorização com motivo escrito. Previsão de conclusão com o confronto planejado × realizado.

**Termina quando:** abrir o app de manhã substitui decidir o que estudar.

### Fase 5 — O assistente

Porta de IA. Primeiro caso: **propor a árvore de tópicos de um edital colado** — é o maior trabalho
manual do sistema e onde a IA tem o melhor retorno. Depois: propor cartões a partir de uma nota.
Sempre propor, nunca gravar.

**Termina quando:** importar um edital deixa de ser uma tarde de digitação.

### 1.0

As cinco fases, com o vault compartilhado funcionando e o backup levando tudo — conhecimento e registro.
A definição de 1.0 não é "tem todas as funcionalidades": é **"dá para apagar o servidor e a pessoa não
perde nada."**

---

## O que este documento decide não fazer

Registrado para que a ausência seja escolha, e não descuido descoberto em uso:

- **Banco de questões próprio.** É outro produto, com custo de conteúdo e jurídico. O sistema trabalha
  sobre o material do usuário, e o registro de desempenho aceita "fonte" como texto livre justamente para
  conviver com o QConcursos da vida sem competir com ele.
- **Correção automática de questões.** Exige o enunciado estruturado, que exige o banco de questões.
- **Gamificação.** Ver a seção de estatísticas.
- **Colaboração ao vivo.** O compartilhamento avisa e grava ao lado; cursor do outro na tela é outro
  projeto.
- **Mapa mental gerado por IA.** O grafo já existe e é derivado do que a pessoa escreveu — que é melhor,
  porque é o mapa dela e não o de um modelo.
