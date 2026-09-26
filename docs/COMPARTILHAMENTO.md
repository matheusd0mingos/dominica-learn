# Compartilhar uma matéria

Como uma pessoa dá a outra acesso a parte do seu vault, e por que cada decisão
foi tomada assim.

Este documento existe antes do código de propósito: compartilhamento mexe na
única fronteira do sistema que, se falhar, entrega o conhecimento de alguém
para outra pessoa sem dar erro nenhum.

---

## As três decisões

Foram tomadas explicitamente, e as alternativas ficaram registradas porque um
dia alguém vai querer mudá-las e precisa saber o que foi pesado.

### 1. Compartilha-se uma PASTA, não o vault

Você compartilha "Contabilidade Avançada"; o resto do seu vault continua
invisível para quem recebeu.

**Por quê:** ninguém quer expor o vault inteiro para emprestar um resumo. O
vault de uma pessoa tem anotação de terapia ao lado de resumo de licitação.
Compartilhar tudo ou nada faria a resposta ser sempre "nada".

**O que isso custa, e é caro:** hoje a fronteira entre pessoas é **uma linha**
— `RaizDoVaultDoUsuario` devolve `{raiz}/{apelido}` e um adaptador que
esquecesse de pedi-la simplesmente não teria caminho para escrever. Com pasta
compartilhada, a fronteira passa a depender do **caminho**, e "esta pessoa pode
escrever aqui?" vira uma pergunta que precisa ser feita em todo caminho de
escrita. Uma pergunta que dá para esquecer é exatamente o tipo de coisa que
este projeto evita.

**Como o custo é contido:** ver "O desenho" abaixo — a resposta é não deixar a
pergunta ser esquecível, e não confiar em lembrar de fazê-la.

### 2. Dois papéis, por convite: leitor e editor

O dono escolhe pessoa a pessoa.

**Por quê:** os dois casos são reais e diferentes. "Emprestei meu resumo para a
turma" é leitura; "montamos o material juntos" é escrita. Um papel só
atenderia um dos dois.

**Descartado — todo mundo que entra escreve:** faria o link de convite valer
tanto quanto uma senha, e tiraria a possibilidade de emprestar sem arriscar.

### 3. Conflito avisa e grava ao lado — nunca perde

Quando duas pessoas gravam a mesma nota, a segunda vê "Fulano mudou esta nota"
e escolhe: recarregar, ou gravar a versão dela ao lado.

**Por quê:** a impressão digital que já detecta conflito entre abas serve para
isto sem nenhuma invenção nova. E a falha que a promessa "o vault é seu" menos
suporta é o parágrafo de alguém sumir sem ninguém ver.

**Descartado — última gravação vence:** é o que o app faz hoje entre abas, e
entre abas é aceitável porque é a mesma pessoa. Com outra pessoa do outro lado,
é perda silenciosa.

**Descartado — mesclar linha a linha, como o git:** funciona quase sempre, e é
o "quase" que mata. Quando erra, produz texto que parece certo. Num vault de
estudo ninguém relê a nota inteira para conferir.

**Descartado — travar a nota:** sem conflito por construção, mas a trava
esquecida bloqueia o grupo até expirar.

**O que isso custa:** o vault pode acumular "Licitações (versão de Ana).md" que
alguém precisa reconciliar à mão. É um custo visível, e visível é o ponto.

---

## O desenho

### O compartilhamento não é conhecimento — e não vai para o índice

A regra do projeto é "nada que exista só no índice pode ser conhecimento do
usuário". Aqui vale a recíproca, e ela é mais dura: **o compartilhamento não é
conhecimento, e por isso não pode viver no índice**.

O índice é derivado e descartável — o próprio README manda apagá-lo e
reindexar quando algo estiver estranho. Se a concessão morasse lá, um
"reindexar do zero" revogaria o acesso de todo mundo, ou pior: um reindexar
mal-feito poderia recriá-lo errado. Concessão é autorização, e autorização mora
com a identidade.

Vai para o banco de **identidade**, ao lado das contas.

E o inverso também precisa ficar dito: o compartilhamento **também não vai para
o frontmatter da nota**. Colocá-lo no arquivo pareceria coerente com "tudo mora
no disco", mas significaria que quem recebe uma cópia do `.md` recebe junto a
lista de quem mais tem acesso — e que editar um arquivo em um editor de texto
concede acesso a si mesmo.

### O acesso é um endereço diferente, não o mesmo endereço com um "se"

A tentação é fazer `RaizDoVaultDoUsuario` às vezes devolver a raiz de outra
pessoa. É a mudança menor e é a errada: transformaria uma propriedade
incondicional ("este código só alcança o meu vault") numa condicional, e toda
garantia provada até aqui passaria a depender de a condição estar certa.

Em vez disso, o material compartilhado tem endereço próprio:

```
/notas/{caminho}                          o meu vault — sem mudança nenhuma
/compartilhado/{dono}/{pasta}/{caminho}   o que compartilharam comigo
```

Consequências, todas desejadas:

- `RaizDoVaultDoUsuario` continua significando exatamente o que significa hoje,
  e os testes de isolamento continuam valendo sem alteração;
- para chegar ao material do outro é preciso passar por um caminho que **exige
  apresentar a concessão** — não existe "esqueci de verificar", porque sem a
  concessão não se obtém pasta nenhuma;
- na tela, você **sempre sabe de quem é a nota** que está editando. Material
  compartilhado que se parece com o seu é como se escreve sem querer na nota de
  outra pessoa.

### A decisão de acesso é pura e fica no domínio

`PermissaoNoVault` responde, sem tocar disco nem banco: dado quem eu sou, de
quem é o vault, qual caminho eu quero e quais concessões existem — posso ler?
posso escrever?

Ser pura é o que permite testar cada forma de ataque com uma string, sem subir
Postgres. E é onde mora a armadilha principal: **"Contabilidade Avançada 2" não
está dentro de "Contabilidade Avançada"**. Comparação por prefixo de texto é o
erro clássico aqui, e ele não dá erro — concede acesso a mais do que devia.

### O que o convidado vê quando um link aponta para fora

Uma nota compartilhada quase sempre cita outras que não foram compartilhadas.
`[[Essência sobre a forma]]` pode estar na pasta; `[[Direito/Licitações]]` não
está.

Para o convidado, esse link **não** é uma ligação quebrada. Ligação quebrada,
neste produto, é convite para criar a nota que falta — e criar a nota de outra
pessoa é o contrário do que deveria acontecer. Ele aparece como *fora do que
foi compartilhado*: sem link, com a explicação.

---

## O que esta versão NÃO faz

Escrito para que a ausência seja decisão, e não descuido descoberto em uso:

- **não há edição simultânea ao vivo.** Duas pessoas na mesma nota ao mesmo
  tempo produzem o aviso de conflito, não um cursor do outro na tela;
- **não há histórico de quem escreveu o quê.** O histórico existente é por
  nota, não por autor;
- **não há notificação.** Você descobre que alguém mudou algo quando abre;
- **compartilhar não copia.** Se o dono revogar o acesso, o convidado perde a
  vista — o que ele quiser guardar, precisa ter levado antes pelo backup.

### A fila de revisão ignora material compartilhado

Esta é a limitação mais afiada do desenho, e ela apareceu escrevendo este
documento — não em uso, o que é a única razão de não ter virado um estrago.

O agendamento do flashcard mora **dentro do arquivo**, na marca
`<!--SR:!2026-08-08,1,250-->`. Foi a decisão certa: é o que faz o mesmo cartão
continuar revisável no Obsidian, com o histórico construído aqui.

Mas o arquivo é **um só**. Se o convidado revisasse um cartão da pasta
compartilhada, a marca seria gravada no arquivo do dono — apagando o
agendamento *dele*. Duas pessoas estudando o mesmo material se destruiriam o
histórico mutuamente, a cada resposta, e sem erro nenhum: os dois veriam a fila
funcionando e nenhum dos dois entenderia por que os intervalos nunca crescem.

Não há lugar natural para o agendamento de uma pessoa sobre o arquivo de outra:
no arquivo do dono ele colide, e fora do arquivo ele deixa de ser conhecimento
do usuário — que é a regra que rege o projeto inteiro.

**A decisão, então:** a fila de revisão só pega cartões do **seu próprio**
vault. Os cartões da pasta compartilhada aparecem na leitura da nota, com a
explicação de que revisar ali gravaria no vault de quem compartilhou. Quem
quiser estudá-los de verdade copia o que interessa para o próprio vault — e aí
o agendamento é dele, no arquivo dele, como sempre foi.

É menos do que se gostaria e é o que dá para prometer sem mentir. Um
agendamento por leitor exigiria uma identidade estável de cartão que hoje não
existe (cartão é nota mais linha, e ele se move quando o texto muda) — é um
projeto próprio, e não um detalhe deste.
