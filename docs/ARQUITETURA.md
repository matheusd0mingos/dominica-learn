# Dominica Learn — arquitetura

Este documento explica **por que** o sistema é assim. O código explica o *como*.

A pergunta que rege toda decisão registrada aqui é a da especificação: *"esta implementação torna o
sistema mais fácil de manter daqui a cinco anos?"*

---

## A decisão que define todas as outras

A especificação pede duas coisas que, juntas, definem o produto inteiro:

1. as notas ficam em arquivos `.md` no storage, não no banco;
2. o vault tem de abrir no **Obsidian Desktop**.

A segunda implica que **os arquivos mudam pelas costas da aplicação** — por um Obsidian aberto no
notebook, por um `git pull`, por um sincronizador de nuvem, por um `mv` no terminal. Isso não é cenário
excepcional: é o uso normal.

Daí sai a regra que atravessa o sistema todo:

> **O disco é a fonte da verdade. O índice é derivado e descartável.**

Apagar o Postgres e reconstruí-lo a partir do vault tem de ser rotina, não desastre. O corolário é
prático e vale mais que a regra: **nada que exista só no índice pode ser conhecimento do usuário.** Se um
dado não sobrevive a um *reindexar do zero*, ele não pertence ao índice — pertence ao `.md`.

Isso já tem consequência para o roadmap. Revisão espaçada, flashcards e banco de erros vão precisar
decidir, cada um, entre morar no frontmatter da nota (sobrevive, e abre no Obsidian) ou no banco (não
sobrevive). Não é detalhe de implementação: é decisão de produto, e é melhor tomá-la de olhos abertos.

A única exceção deliberada é o **histórico de revisões** (`IHistoricoDeNotas`), que guarda texto que não
existe mais em arquivo nenhum. Por isso é uma **porta separada** do índice, apesar de compartilhar o
banco: para que nenhuma rotina de manutenção o apague por associação.

---

## Camadas

```
Domain          ← regras. Sem I/O, sem framework, sem dependência de pacote nenhum.
   ↑
Application     ← casos de uso + PORTAS (interfaces). Depende só do Domain.
   ↑
Infrastructure  ← ADAPTADORES: disco, Postgres, vigia de arquivos, relógio.
   ↑
Web             ← Blazor Server, MudBlazor, interop do editor.
```

As setas são a direção das *referências de projeto*, e elas só apontam para dentro. O compilador garante
o que a documentação promete: `Domain` não tem como referenciar `Infrastructure` porque a referência não
existe no `.csproj`.

**O teste vivo da arquitetura é `RegistroDaInfraestrutura.cs`.** É o único lugar onde porta encontra
adaptador. Se um dia trocar Postgres por SQLite, ou disco por S3, exigir mudar algo fora daquele arquivo,
o acoplamento vazou.

### O que está no domínio, e por quê

| Peça | Por que é domínio |
|---|---|
| `AnalisadorDeNota` | interpretar Markdown é regra de negócio: o que conta como etiqueta, o que conta como ligação |
| `ResolvedorDeWikilinks` | "[[Licitações]]" → qual nota é uma decisão do produto (ordem de preferência, desempate) |
| `Reconciliador` | decidir o que mudou entre disco e índice é a regra central; **função pura** |
| `ReescritorDeLigacoes` | o que preservar ao renomear (rótulo, seção, "altura" do link) é decisão de produto |
| `CaminhoNota` | um caminho de nota é sempre relativo e sempre para dentro do vault |
| `ApelidoDoUsuario` | o apelido é o nome da pasta do vault, então precisa ser um segmento de caminho seguro |
| `Materia` | a matéria de uma nota é a primeira pasta do caminho dela |

Todas essas são funções puras ou objetos imutáveis. É o que permite testar "pasta inteira movida",
"conteúdo duplicado trocando de lugar" e "Windows sincronizando com Linux" com duas listas literais, sem
tocar em disco. **A maioria dos testes não precisa de banco nenhum.**

---

## Decisões e seus preços

### A identidade da nota é o caminho, não um GUID

Um GUID não existe no disco. Identificar a nota por ele exigiria um mapa GUID↔arquivo que quebra no
instante em que alguém renomeia por fora — o cenário que a promessa "abre no Obsidian" garante que vai
acontecer.

**Preço:** renomear muda a identidade. Pago em `ServicoDeNotas.RenomearAsync`, que reescreve os wikilinks
de entrada. Sem essa contrapartida, renomear quebraria toda a rede de ligações — que, num vault de
estudo, **é** o conhecimento.

### A fronteira entre pessoas é uma pasta e uma coluna

Cada usuário tem uma **pasta própria** dentro da raiz dos vaults (`/dados/vault/{apelido}`) e **uma
coluna `Usuario`** em cada tabela do índice. O apelido é escolhido no cadastro e é imutável: ele *é* o
caminho, e trocá-lo significaria mover a árvore no disco e reescrever toda linha do índice — possivelmente
com o Obsidian de alguém aberto no meio.

O isolamento é aplicado em **dois lugares únicos**, nunca espalhado:

- no disco, por `RaizDoVaultDoUsuario` — um adaptador que esqueça de pedir a raiz não tem caminho onde
  escrever;
- no banco, por **filtro global do EF Core** — uma consulta que esqueça de declarar o usuário devolve
  **vazio**, nunca a nota de outra pessoa.

Essa assimetria é deliberada. Vazamento de vault não dá erro, não aparece em log e só é descoberto pelo
dono da nota; a única forma aceitável de falhar aqui é **fechado**.

O caminho da nota é único *dentro de* um vault, e não no banco: duas pessoas estudando para a mesma prova
vão ambas criar `Direito/Licitações.md`, e isso tem de funcionar.

### A data de modificação não decide nada; a impressão digital decide

Sincronizador de nuvem e `git checkout` reescrevem `mtime` de arquivos intactos. Se a data decidisse, o
vault inteiro pareceria alterado depois de qualquer uma dessas operações. A impressão (SHA-256 do
conteúdo, com quebras normalizadas) é o que distingue mudança real de ruído — e é também o que detecta
**renomeação**: sumiu de um caminho, apareceu em outro, mesmo conteúdo.

**Limite conhecido:** mudar nome *e* conteúdo entre duas execuções não é rastreável. Rastrear exigiria um
identificador dentro do arquivo, o que sujaria o `.md` e quebraria a promessa do Obsidian. Está
documentado num teste para que ninguém "conserte" por acidente.

### Reconciliação completa, não incremental por evento

O `FileSystemWatcher` perde eventos sob carga — é limitação conhecida dele. Um índice construído a partir
de uma sequência de eventos com buracos fica sutilmente errado, e ninguém percebe. Comparar disco e
índice inteiros é a operação que **se autocorrige**: erre um evento e a próxima passada conserta.

O vigia espera o disco ficar em silêncio (750 ms) antes de agir, porque salvar um arquivo gera três ou
quatro eventos e um `git checkout` gera milhares.

### Blazor Server, e não WebAssembly

Primeiro carregamento sem baixar runtime, memória mínima no cliente, acesso direto ao vault e ao Postgres
a partir dos componentes. O CodeMirror trata a digitação no cliente, então a latência do SignalR não
afeta o ato de escrever.

**Preço:** exige conexão viva; não há offline. WASM não resolveria isso de graça — o vault mora no
servidor, então "offline" seria ficção sem construir sincronização de verdade.

### JavaScript existe, e está confinado

"Evitar JavaScript" e "usar CodeMirror" se contradizem: CodeMirror *é* JavaScript, assim como Mermaid,
KaTeX e o graph view que virão. O que dá para garantir é o **tamanho da superfície**: um módulo
(`wwwroot/js/editor.js`), uma porta C# (`IEditorDeTexto`), e nenhum componente Razor chamando
`IJSRuntime` direto para o editor. Trocar CodeMirror por Monaco mexe em dois arquivos, não em vinte
páginas.

### Resultado em vez de exceção

"A nota não existe" e "o arquivo mudou por fora" não são situações excepcionais neste produto — são o
cotidiano. Exceção para fluxo esperado transforma o caminho normal em stack trace e custa caro num
autosave que dispara a cada poucos segundos.

### Conflito não é resolvido sozinho

Se o disco mudou desde que o editor carregou, `SalvarAsync` **recusa** e devolve `Conflito`. O outro lado
pode ser o Obsidian do notebook com meia hora de trabalho. Quem decide é quem está na frente da tela —
há o botão de recarregar e o de gravar por cima.

---

## O que ainda não existe

Entregue: núcleo do vault (arquivos, análise, ligações, backlinks, etiquetas, busca, autosave, histórico,
reconciliação, renomeação com reescrita de links), Docker, configuração, segurança, observabilidade.

**Não entregue, de propósito:** graph view, Mermaid, KaTeX, templates, favoritos, upload de arquivos,
code highlight. E nada da lista de concursos (revisão espaçada, flashcards, banco de erros, IA).

A arquitetura os acomoda — as portas estão desenhadas para isso. Mas cada um deve ser construído *depois*
de a ferramenta ser usada de verdade por alguns dias, porque o uso muda a prioridade. O motivo de o
núcleo vir primeiro é exatamente esse: dá para usar amanhã, e o que for feito em cima já nasce corrigido
pela experiência de quem usou.

### Dívidas registradas

| Item | Situação |
|---|---|
| `'unsafe-inline'` em `script-src` | concessão conhecida: o Blazor Server injeta script inline. Sai com *nonce* por requisição. |
| Busca com `ILIKE` | funciona até uns milhares de notas. Vira `tsvector` sem mexer na porta `IIndiceDoVault`. |
| Blocos de código por indentação (4 espaços) | não detectados: exigiria um parser CommonMark completo dentro do domínio. Blocos cercados funcionam. |
| Rastreamento do Npgsql no OpenTelemetry | fora por colisão de sobrecarga com o EF Core; volta junto com o coletor. |
| Frontmatter | subconjunto declarado de YAML (escalar, lista em linha, lista em bloco). O resto é preservado no arquivo e não interpretado. |

---

## Como rodar

**Desenvolvimento** (precisa de um Postgres local):

```bash
cd novo/learn
dotnet run --project src/Dominica.Learn.Web
```

**Testes** (não precisam de banco nem de rede):

```bash
dotnet test Dominica.Learn.slnx
```

**Produção:** copie `.env.exemplo` para `.env`, aponte `VAULT_NO_HOST` para a pasta que você abre no
Obsidian, e `docker compose up -d`. A aplicação não publica porta no host — quem fala com a internet é o
proxy reverso, que termina TLS.
