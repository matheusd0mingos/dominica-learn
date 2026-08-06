namespace Dominica.Learn.Web.Tema;

/// <summary>
/// As cores com que o grafo pinta as matérias.
///
/// POR QUE SÃO EXATAMENTE QUATRO, E NÃO OITO OU DOZE:
///
/// O grafo é uma forma de DISPERSÃO — dois pontos de matérias diferentes podem cair lado a lado em
/// qualquer lugar da tela. Isso significa que TODO par de cores precisa ser distinguível, e não apenas os
/// pares vizinhos de uma legenda (que é o requisito, bem mais frouxo, de uma barra empilhada).
///
/// Rodando o validador de paleta sobre todos os pares, nos dois temas, dos oito tons da paleta padrão:
/// nenhum conjunto de cinco ou mais passa. Verde↔laranja tem ΔE 3.2 para quem tem protanopia; vermelho↔
/// laranja tem ΔE 7.1 até para visão normal, abaixo do piso 15. Só dois conjuntos de quatro passam, e
/// este é um deles:
///
///   claro:  azul #2a78d6 · amarelo #eda100 · magenta #e87ba4 · verde #008300
///   escuro: azul #3987e5 · amarelo #c98500 · magenta #d55181 · verde #008300
///
/// Piores pares — claro: CVD ΔE 13,0 e visão normal 19,6. Escuro: CVD ΔE 6,9 e visão normal 19,3.
///
/// O ΔE 6,9 do escuro cai na faixa 6–8, que só é aceitável COM CODIFICAÇÃO SECUNDÁRIA. E o amarelo e o
/// magenta do tema claro ficam abaixo de 3:1 contra o fundo, o que exige o mesmo alívio. As duas dívidas
/// são pagas pelas mesmas três coisas, e nenhuma delas é decorativa — se alguém removê-las, a paleta
/// passa a estar em desacordo:
///   1. legenda sempre visível, nomeando cada matéria;
///   2. rótulo direto nos nós que importam, e o nome da matéria no "title" de todos;
///   3. o próprio AGRUPAMENTO ESPACIAL, que a coesão por matéria produz no layout.
///
/// Da quinta matéria em diante, a cor deixa de ser confiável — então elas não recebem cor: ficam no tom
/// neutro e a legenda diz isso em voz alta. Quem quiser ver uma delas destacada clica na legenda e isola,
/// que é a saída correta (facetar) em vez de inventar um quinto tom que ninguém consegue distinguir.
/// </summary>
public static class PaletaDeMaterias
{
    /// <summary>Quantas matérias recebem cor própria. Não aumente sem rodar o validador de novo.</summary>
    public const int Coloridas = 4;

    private static readonly string[] Claro = ["#2a78d6", "#eda100", "#e87ba4", "#008300"];
    private static readonly string[] Escuro = ["#3987e5", "#c98500", "#d55181", "#008300"];

    /// <summary>Tom das matérias sem cor própria e do "sem matéria" — recessivo, nunca competindo.</summary>
    public static string Neutra(bool escuro) => escuro ? "#8a8a83" : "#9a9a92";

    /// <summary>
    /// Cor do enésimo slot. <paramref name="slot"/> fora da faixa devolve o neutro — a chamada não
    /// precisa lembrar do limite, e esquecer disso pintaria a nona matéria com a cor da primeira.
    /// </summary>
    public static string Cor(int slot, bool escuro) =>
        slot < 0 || slot >= Coloridas ? Neutra(escuro) : (escuro ? Escuro : Claro)[slot];
}
