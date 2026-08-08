// O teclado da revisão: espaço vira o cartão, 1/2/3/4 respondem.
//
// POR QUE ISTO EXISTE: responder cartão é o gesto mais repetido do produto — dezenas de vezes por dia,
// em sequência. Obrigar um clique de mouse por cartão transforma vinte minutos de revisão em vinte
// minutos de mira; é o básico do Anki, e era o que faltava aqui.
//
// NO DOCUMENTO, com capture, pelo mesmo motivo do atalhos.js: o atalho tem de funcionar com o foco em
// qualquer lugar da tela — inclusive logo depois de um clique num botão, quando o foco ficou nele.
//
// QUEM DECIDE O QUE A TECLA FAZ É O C#. Este módulo só reporta "espaço" ou o dígito: se o cartão já
// está virado, se há fila, se está gravando — tudo isso é estado do componente, e duplicá-lo aqui
// seria a segunda cópia que fica para trás.

let ouvinte = null

export function ligar(referencia) {
  desligar()

  ouvinte = (e) => {
    // Modificador apertado = outra intenção (Ctrl+1 troca de aba no navegador; não é nosso).
    if (e.ctrlKey || e.metaKey || e.altKey) return

    // DENTRO DE CAMPO OU DIÁLOGO, NADA. A tela abre diálogos com texto ("Errei uma questão", "Cartão
    // novo") — um espaço digitado ali é um espaço da frase, não um comando. Sem esta guarda, escrever
    // "art. 5" num diálogo responderia o cartão por baixo dele.
    const alvo = e.target
    if (alvo && alvo.closest && alvo.closest('input, textarea, [contenteditable="true"], .mud-dialog')) return

    const tecla = e.key === ' ' ? 'espaco'
      : ['1', '2', '3', '4'].includes(e.key) ? e.key
      : null
    if (!tecla) return

    // preventDefault no espaço é obrigatório: sem ele a página rola meia tela a cada cartão virado.
    // Nos dígitos, evita o "quick find" de alguns navegadores/leitores.
    e.preventDefault()
    referencia.invokeMethodAsync('AoTeclarNaRevisaoAsync', tecla)
  }

  document.addEventListener('keydown', ouvinte, { capture: true })
}

export function desligar() {
  if (!ouvinte) return
  document.removeEventListener('keydown', ouvinte, { capture: true })
  ouvinte = null
}
