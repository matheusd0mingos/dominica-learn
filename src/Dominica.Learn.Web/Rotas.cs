using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Web;

/// <summary>
/// As URLs internas do app, montadas num lugar só.
///
/// EXISTE POR CAUSA DE UM DEFEITO ESPECÍFICO, e o motivo precisa estar escrito aqui porque ele volta
/// sozinho toda vez que alguém escreve uma navegação nova.
///
/// <c>NavigateTo("/notas/…")</c> parece certo e está errado quando o app é servido sob um caminho. O
/// Blazor resolve a URL com as regras de URI: <c>new Uri(base, relativa)</c>. Com a base valendo
/// <c>https://dominio/private/dominica-learn/</c>, uma relativa que COMEÇA COM BARRA é resolvida contra
/// a RAIZ DO DOMÍNIO e o sub-caminho é descartado —
/// <c>https://dominio/notas/…</c>, que dá 404. Sem a barra, cai no lugar certo.
///
/// Em desenvolvimento o defeito é invisível: sem sub-caminho, a base já é a raiz e as duas formas dão
/// no mesmo. Ele só aparece em produção, e como 404 numa navegação que o usuário acabou de disparar —
/// "criei a nota e o app me jogou numa página de erro".
///
/// A segunda razão é o ESCAPE. O nome da nota vem do usuário e tem espaço, acento e, um dia, um <c>#</c>
/// ou um <c>?</c> — que num URL significam âncora e query. Escapar segmento a segmento resolve isso sem
/// destruir as barras, que a rota curinga <c>/notas/{*caminho}</c> precisa preservar.
/// </summary>
public static class Rotas
{
    /// <summary>A tela de uma nota. Relativa de propósito — ver o comentário da classe.</summary>
    public static string Nota(CaminhoNota caminho) => Nota(caminho.Valor);

    /// <inheritdoc cref="Nota(CaminhoNota)"/>
    public static string Nota(string caminho) =>
        "notas/" + string.Join('/', caminho.Split('/').Select(Uri.EscapeDataString));

    /// <summary>O grafo, opcionalmente recortado numa matéria.</summary>
    public static string Grafo(Materia? materia = null) =>
        materia is null || materia == Materia.Nenhuma
            ? "grafo"
            : $"grafo?materia={Uri.EscapeDataString(materia.Nome)}";
}
