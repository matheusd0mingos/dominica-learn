namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// Onde o Learn é servido dentro do endereço.
///
/// Vazio (padrão) = na raiz do domínio, como <c>learn.SEU_DOMINIO</c>.
/// Preenchido = sob um caminho, como <c>/private/dominica-learn</c>, atrás do proxy da plataforma.
///
/// POR QUE ISSO É CONFIGURAÇÃO E NÃO CÓDIGO: a escolha entre "módulo da Dominica" e "produto vizinho" é
/// de produto e pode mudar. Se o caminho estivesse fixo no fonte, mudar de ideia exigiria recompilar — e
/// as duas formas exigem exatamente os mesmos ajustes, então não há motivo para escolher no build.
///
/// O QUE ISTO LIGA, e por que cada peça é necessária:
///
/// 1. <c>UsePathBase</c>, para o roteamento enxergar "/notas" quando a URL é "/private/…/notas".
/// 2. <c>&lt;base href&gt;</c> no HTML, que é o que faz link RELATIVO resolver para dentro do caminho.
/// 3. Todo link do app é relativo (sem barra inicial). Link com barra inicial é relativo à RAIZ do
///    documento e ignora o <c>&lt;base&gt;</c> — ele sairia do sub-caminho e daria 404. Essa é a parte
///    que quebra em silêncio quando alguém escreve href="/grafo" por hábito.
/// 4. Nome e caminho próprios para o cookie, senão o Learn e a plataforma disputam o mesmo cookie no
///    mesmo domínio e um derruba a sessão do outro.
/// </summary>
public sealed class OpcoesDeHospedagem
{
    public const string Secao = "Hospedagem";

    private string _caminhoBase = string.Empty;

    /// <summary>
    /// O prefixo, com barra inicial e sem barra final: <c>/private/dominica-learn</c>.
    /// Normalizado na atribuição para que "private/learn/" e "/private/learn" deem no mesmo.
    /// </summary>
    public string CaminhoBase
    {
        get => _caminhoBase;
        set
        {
            var t = (value ?? string.Empty).Trim().Trim('/');
            _caminhoBase = t.Length == 0 ? string.Empty : "/" + t;
        }
    }

    /// <summary>O que vai no <c>&lt;base href&gt;</c> — sempre com barra final, que o HTML exige.</summary>
    public string BaseHref => CaminhoBase.Length == 0 ? "/" : CaminhoBase + "/";

    /// <summary>
    /// Nome do cookie de sessão. Distinto do da plataforma de propósito: no mesmo domínio, dois apps com
    /// cookie de mesmo nome se derrubam — e o sintoma é "fui deslogado sozinho", que ninguém liga a isso.
    /// </summary>
    public string NomeDoCookie { get; set; } = "dominica_learn_auth";
}
