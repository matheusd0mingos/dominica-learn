// Copiar um texto para a área de transferência.
//
// DEVOLVE FALSO EM VEZ DE LANÇAR, e é disso que o chamador precisa. A API de clipboard falha de três
// jeitos que não são defeito nosso e que a pessoa não consegue resolver: fora de contexto seguro (http
// num endereço que não seja localhost) ela nem existe; o navegador pode negar a permissão; e alguns
// recusam quando a chamada não veio de um gesto direto do usuário. Em todos eles o certo é a tela dizer
// "copie à mão" com o endereço à mostra — e por isso o diálogo mostra a URL num campo selecionável,
// mesmo quando o botão funciona.
//
// O `await` É OBRIGATÓRIO aqui, e não estilo: `writeText` devolve uma promessa que REJEITA quando a
// permissão é negada. Sem esperá-la, a função devolveria `true` e a rejeição viraria um erro solto no
// console — a tela diria "copiado" para um clipboard vazio, que é o pior desfecho possível para um
// botão de copiar link.
export async function copiar(texto) {
  try {
    if (!navigator.clipboard) return false
    await navigator.clipboard.writeText(texto)
    return true
  } catch (e) {
    return false
  }
}
