using Dominica.Learn.Domain.Analise;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Ligacoes;

/// <summary>
/// Reescreve as ligações de uma nota quando OUTRA nota é renomeada ou movida.
///
/// POR QUE ISTO É O CORAÇÃO DO RENOMEAR, e não um detalhe dele:
///
/// Num vault, renomear um arquivo é trivial — o sistema de arquivos faz. O que quebra são os
/// <c>[[links]]</c> que apontavam para ele, espalhados por notas que o usuário nem lembra que existem.
/// Um app que renomeia sem consertar os links deixa um rastro de ligações mortas que só aparece meses
/// depois, quando a pessoa clica e não vai a lugar nenhum. É a forma mais silenciosa de perder
/// conhecimento: nada some do disco, mas o caminho até ele some.
///
/// A REGRA DE REESCRITA É A DO OBSIDIAN, e ela não é "troque o texto todo":
///
///   [[Licitações]]           só o nome    → só muda se o NOME mudou
///   [[Direito/Licitações]]   com a pasta  → muda se a pasta OU o nome mudarem
///
/// Manter a forma curta importa porque ela é uma escolha do autor: quem escreveu <c>[[Licitações]]</c>
/// quis a forma curta, e trocá-la por <c>[[Direito Administrativo/Licitações]]</c> só porque a nota
/// mudou de pasta polui o texto que a pessoa lê todo dia. Se a forma curta continua resolvendo para a
/// nota certa, ela fica como está.
///
/// A CLASSE É PURA: recebe texto e devolve texto. Não conhece disco, índice nem usuário. Quem sabe
/// QUAIS notas apontam para a nota movida é o caso de uso, que tem o índice; aqui só se sabe reescrever.
/// </summary>
public static class ReescritorDeLigacoes
{
    /// <summary>
    /// Devolve o conteúdo com as ligações que apontavam para <paramref name="de"/> apontando para
    /// <paramref name="para"/>.
    ///
    /// Devolve A MESMA INSTÂNCIA quando nada mudou. Isso não é economia de memória: é o sinal que
    /// permite ao chamador não regravar o arquivo — e não regravar é o que evita acordar o vigia do
    /// vault, sujar a data de modificação e criar uma revisão falsa em cada nota do vault.
    /// </summary>
    /// <param name="resolver">
    /// Opcional. Com ele, só é reescrita a ligação que REALMENTE resolve para a nota movida — o que
    /// importa quando duas notas têm o mesmo nome em pastas diferentes. Sem ele, casa-se pelas formas
    /// de escrever o caminho, que é o que dá para fazer sem conhecer o vault.
    /// </param>
    public static string Reescrever(
        string conteudo, CaminhoNota de, CaminhoNota para, Func<string, CaminhoNota?>? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        resolver ??= PelasFormasDeEscrever(de);

        if (de == para) return conteudo;

        var analise = AnalisadorDeNota.Analisar(conteudo, de.Nome);

        // De trás para a frente: recortar pelo índice invalida todas as posições seguintes. Indo do fim
        // para o começo, cada troca só mexe em texto que já foi visitado.
        var ligacoes = analise.LigacoesInternas
            .Where(l => l.Comprimento > 0)
            .OrderByDescending(l => l.Posicao)
            .ToList();

        var texto = conteudo;
        var mudou = false;

        foreach (var ligacao in ligacoes)
        {
            if (resolver(ligacao.Alvo) != de) continue;

            var alvoNovo = AlvoReescrito(ligacao, de, para, resolver);
            if (alvoNovo is null) continue;

            var escrita = Escrever(ligacao, alvoNovo);
            var fim = ligacao.Posicao + ligacao.Comprimento;
            if (ligacao.Posicao < 0 || fim > texto.Length) continue;   // texto mudou sob nossos pés
            if (texto[ligacao.Posicao..fim] == escrita) continue;

            texto = string.Concat(texto.AsSpan(0, ligacao.Posicao), escrita, texto.AsSpan(fim));
            mudou = true;
        }

        return mudou ? texto : conteudo;
    }

    /// <summary>Quantas ligações desta nota apontam para o caminho dado.</summary>
    public static int Contar(string conteudo, CaminhoNota alvo, Func<string, CaminhoNota?>? resolver = null)
    {
        resolver ??= PelasFormasDeEscrever(alvo);
        return AnalisadorDeNota.Analisar(conteudo, alvo.Nome).LigacoesInternas.Count(l => resolver(l.Alvo) == alvo);
    }

    /// <summary>
    /// O resolvedor de pobre, para quando não há vault à mão: reconhece as quatro formas com que
    /// alguém pode ter escrito um link para esta nota. Não distingue duas notas de mesmo nome em
    /// pastas diferentes — por isso o resolvedor de verdade é preferível quando existe.
    /// </summary>
    private static Func<string, CaminhoNota?> PelasFormasDeEscrever(CaminhoNota nota)
    {
        var formas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nota.Nome, nota.Nome + CaminhoNota.Extensao, nota.Valor, SemExtensao(nota.Valor),
        };
        return alvo => formas.Contains(alvo.Trim()) ? nota : null;
    }

    /// <summary>
    /// O novo texto do alvo, preservando a forma que o autor escolheu. Null quando não é preciso mexer.
    /// </summary>
    private static string? AlvoReescrito(
        Wikilink ligacao, CaminhoNota de, CaminhoNota para, Func<string, CaminhoNota?> resolver)
    {
        // A ligação em forma de Markdown — [rótulo](Direito/Licitações.md) — carrega a extensão e o
        // caminho completo. Ali a forma curta não existe, então o alvo novo é o caminho novo inteiro.
        if (ligacao.Forma == FormaDaLigacao.Markdown)
            return para.Valor;

        var escreveuComPasta = ligacao.Alvo.Contains('/');

        if (!escreveuComPasta)
        {
            // Forma curta. Se o nome não mudou, a ligação continua resolvendo para a nota certa mesmo
            // depois da mudança de pasta — e mexer nela seria estragar o texto à toa.
            if (string.Equals(de.Nome, para.Nome, StringComparison.Ordinal)) return null;

            // O nome mudou. Continua curto SE isso ainda for inequívoco: se já existe outra nota com o
            // nome novo em outra pasta, a forma curta passaria a apontar para a errada, e aí é preciso
            // escrever o caminho.
            var resolvido = resolver(para.Nome);
            return resolvido is null || resolvido == para ? para.Nome : SemExtensao(para.Valor);
        }

        return SemExtensao(para.Valor);
    }

    /// <summary>Remonta a ligação inteira com o alvo novo, mantendo seção, rótulo e forma.</summary>
    private static string Escrever(Wikilink ligacao, string alvoNovo)
    {
        var secao = ligacao.Secao is null ? "" : "#" + ligacao.Secao;

        return ligacao.Forma switch
        {
            FormaDaLigacao.Markdown =>
                $"[{ligacao.Rotulo ?? ligacao.TextoExibido}]({EscaparParaMarkdown(alvoNovo)}{secao})",
            FormaDaLigacao.Embed =>
                $"![[{alvoNovo}{secao}{Rotulo(ligacao)}]]",
            _ =>
                $"[[{alvoNovo}{secao}{Rotulo(ligacao)}]]",
        };
    }

    private static string Rotulo(Wikilink ligacao) => ligacao.Rotulo is null ? "" : "|" + ligacao.Rotulo;

    private static string SemExtensao(string caminho) =>
        caminho.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? caminho[..^3] : caminho;

    /// <summary>
    /// Espaço num destino de link Markdown fecha o parêntese cedo no leitor de Markdown, e o resto do
    /// caminho vira "título". <c>%20</c> é o que o próprio Obsidian escreve.
    /// </summary>
    private static string EscaparParaMarkdown(string caminho) =>
        caminho.Replace(" ", "%20", StringComparison.Ordinal);
}
