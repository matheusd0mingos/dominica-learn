// Carimba a escolha de tema no <html>, para que o CSS da barra e do Identity a enxerguem.
//
// POR QUE PRECISA DISTO: o MudBlazor troca as variáveis DELE quando o tema muda e não deixa marca
// nenhuma no documento — nem classe, nem atributo. Tudo que é estilizado pelo MudBlazor acompanha; a
// barra do app, que é CSS nosso, ficaria na cor do tema anterior. O sintoma é feio e específico:
// conteúdo escuro sob uma barra que continua clara.
//
// O valor é gravado no localStorage e reaplicado antes da primeira pintura (ver o inline em
// App.razor) para não haver piscada de tela branca em quem escolheu escuro.
export function aplicar(escuro) {
    const tema = escuro ? 'escuro' : 'claro'
    document.documentElement.setAttribute('data-tema', tema)
    try { localStorage.setItem('dominica-learn-tema', tema) } catch { /* modo privado: só não lembra */ }
}

// A preferência guardada, ou null quando nunca houve escolha — aí vale a do sistema operacional.
export function guardado() {
    try { return localStorage.getItem('dominica-learn-tema') } catch { return null }
}
