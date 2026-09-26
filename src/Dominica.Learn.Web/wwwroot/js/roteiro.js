// O ROTEIRO DA NOTA (outline): rola a leitura até o título clicado.
//
// Os índices vêm do C#, contados sobre os cabeçalhos do MARKDOWN da nota. Por isso os títulos que vivem
// dentro de uma transclusão (nota embutida) são filtrados aqui: eles existem no HTML mas não no texto da
// nota aberta, e contá-los desalinharia o clique — o 3º título do roteiro rolaria para o 3º título de
// OUTRA nota.
export function irAte(idDaLeitura, indice) {
  const raiz = document.getElementById(idDaLeitura)
  if (!raiz) return
  const titulos = [...raiz.querySelectorAll('h1,h2,h3,h4,h5,h6')]
    .filter(t => !t.closest('.transclusao'))
  titulos[indice]?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
