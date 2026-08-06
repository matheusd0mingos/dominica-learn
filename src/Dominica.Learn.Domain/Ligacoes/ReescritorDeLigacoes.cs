using System.Text;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Ligacoes;

/// <summary>
/// Reescreve, no texto de uma nota, as ligações que apontavam para um caminho antigo.
///
/// POR QUE ISTO É DOMÍNIO: é uma transformação de texto Markdown regida por regras de negócio (o que
/// conta como "apontar para esta nota", o que preservar). Não tem disco, não tem banco — entra string,
/// sai string. Deixá-la na infraestrutura tiraria do teste a única parte difícil: decidir se
/// "[[Licitações|as regras]]" deve virar "[[Contratos|as regras]]" ou "[[Contratos]]".
///
/// O QUE ELE PRESERVA, E POR QUÊ:
///   • o RÓTULO, sempre. "[[Licitações|as regras de 2021]]" vira "[[Contratos|as regras de 2021]]" — o
///     rótulo é prosa escrita pelo autor no meio de uma frase, e trocá-lo reescreveria o texto dele;
///   • a SEÇÃO. "[[Licitações#Modalidades]]" continua apontando para a mesma seção;
///   • a FORMA de escrever o alvo. Quem escreveu o caminho completo continua com caminho completo; quem
///     escreveu só o nome continua com só o nome. Trocar "[[Licitações]]" por
///     "[[Concursos/Direito/Contratos]]" encheria o texto de caminho onde havia uma palavra.
/// </summary>
public static class ReescritorDeLigacoes
{
    /// <summary>
    /// Devolve o texto com as ligações para <paramref name="de"/> apontando para <paramref name="para"/>.
    /// Quando nada casa, devolve a MESMA instância — o chamador usa isso para não gravar à toa.
    /// </summary>
    public static string Reescrever(string conteudo, CaminhoNota de, CaminhoNota para)
    {
        if (string.IsNullOrEmpty(conteudo)) return conteudo;

        var alvosAntigos = FormasDeEscrever(de);
        var mudou = false;
        var sb = new StringBuilder(conteudo.Length);
        var i = 0;

        while (i < conteudo.Length)
        {
            // —— [[alvo]] / [[alvo#seção|rótulo]] ————————————————————————————————————————————
            if (conteudo[i] == '[' && i + 1 < conteudo.Length && conteudo[i + 1] == '[')
            {
                var fim = conteudo.IndexOf("]]", i + 2, StringComparison.Ordinal);
                if (fim > 0)
                {
                    var interno = conteudo[(i + 2)..fim];
                    var novo = ReescreverWikilink(interno, alvosAntigos, para);
                    if (novo is not null)
                    {
                        sb.Append("[[").Append(novo).Append("]]");
                        i = fim + 2;
                        mudou = true;
                        continue;
                    }
                }
            }

            // —— [rótulo](alvo.md) ————————————————————————————————————————————————————————
            if (conteudo[i] == '[')
            {
                var fechaRotulo = conteudo.IndexOf(']', i + 1);
                if (fechaRotulo > 0 && fechaRotulo + 1 < conteudo.Length && conteudo[fechaRotulo + 1] == '(')
                {
                    var fimUrl = conteudo.IndexOf(')', fechaRotulo + 2);
                    if (fimUrl > 0)
                    {
                        var url = conteudo[(fechaRotulo + 2)..fimUrl];
                        var novoUrl = ReescreverUrlMarkdown(url, alvosAntigos, para);
                        if (novoUrl is not null)
                        {
                            sb.Append(conteudo[i..(fechaRotulo + 2)]).Append(novoUrl).Append(')');
                            i = fimUrl + 1;
                            mudou = true;
                            continue;
                        }
                    }
                }
            }

            sb.Append(conteudo[i]);
            i++;
        }

        return mudou ? sb.ToString() : conteudo;
    }

    private static string? ReescreverWikilink(string interno, HashSet<string> alvosAntigos, CaminhoNota para)
    {
        var rotulo = (string?)null;
        var corpo = interno;
        var barra = interno.IndexOf('|');
        if (barra >= 0) { rotulo = interno[(barra + 1)..]; corpo = interno[..barra]; }

        var secao = (string?)null;
        var cerquilha = corpo.IndexOf('#');
        if (cerquilha >= 0) { secao = corpo[(cerquilha + 1)..]; corpo = corpo[..cerquilha]; }

        var alvo = corpo.Trim();
        if (alvo.Length == 0 || !alvosAntigos.Contains(alvo)) return null;

        // Mantém a "altura" de quem escreveu: nome curto continua curto, caminho continua caminho.
        var novoAlvo = alvo.Contains('/') ? SemExtensao(para.Valor) : para.Nome;
        var reconstruido = novoAlvo;
        if (secao is not null) reconstruido += "#" + secao;
        if (rotulo is not null) reconstruido += "|" + rotulo;
        return reconstruido;
    }

    private static string? ReescreverUrlMarkdown(string url, HashSet<string> alvosAntigos, CaminhoNota para)
    {
        var limpa = url.Trim();
        // link com título — [x](destino "título"): só o destino é reescrito
        var titulo = string.Empty;
        var espaco = limpa.IndexOf(' ');
        if (espaco > 0) { titulo = limpa[espaco..]; limpa = limpa[..espaco]; }

        var secao = (string?)null;
        var cerquilha = limpa.IndexOf('#');
        if (cerquilha >= 0) { secao = limpa[(cerquilha + 1)..]; limpa = limpa[..cerquilha]; }

        var semEscape = limpa.Replace("%20", " ", StringComparison.Ordinal);
        if (!alvosAntigos.Contains(semEscape)) return null;

        var novo = semEscape.Contains('/') ? para.Valor : para.Nome + CaminhoNota.Extensao;
        // espaço em URL de Markdown precisa voltar escapado, senão o link quebra em qualquer renderizador
        novo = novo.Replace(" ", "%20", StringComparison.Ordinal);
        if (secao is not null) novo += "#" + secao;
        return novo + titulo;
    }

    /// <summary>Todas as formas com que alguém pode ter escrito um link para esta nota.</summary>
    private static HashSet<string> FormasDeEscrever(CaminhoNota caminho) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            caminho.Nome,
            caminho.Nome + CaminhoNota.Extensao,
            caminho.Valor,
            SemExtensao(caminho.Valor),
        };

    private static string SemExtensao(string valor) =>
        valor.EndsWith(CaminhoNota.Extensao, StringComparison.OrdinalIgnoreCase)
            ? valor[..^CaminhoNota.Extensao.Length]
            : valor;
}
