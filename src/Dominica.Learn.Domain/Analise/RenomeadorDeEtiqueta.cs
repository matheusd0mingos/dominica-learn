namespace Dominica.Learn.Domain.Analise;

/// <summary>
/// RENOMEIA UMA ETIQUETA dentro do conteúdo de uma nota — no frontmatter e no texto.
///
/// POR QUE ISTO PRECISA EXISTIR: o produto CRIA etiquetas sozinho (#errei-na-prova a cada erro
/// registrado) e convida a digitá-las à mão — e um typo ("#pegadinh") ficava para sempre, sem conserto
/// que não fosse abrir nota por nota. Renomear para uma etiqueta que JÁ existe é a mesclagem: as duas
/// viram uma.
///
/// —— AS DECISÕES ————————————————————————————————————————————————————————————————————
///
/// A HIERARQUIA VAI JUNTO: renomear "#direito" leva "#direito/penal" para "#novo/penal". É a mesma regra
/// de toda consulta de etiqueta do produto — o pai carrega os filhos — e renomear só o pai deixaria a
/// árvore com dois troncos.
///
/// AS POSIÇÕES VÊM DO ANALISADOR (<see cref="AnalisadorDeNota.EtiquetasPosicionadas"/>), não de um regex
/// próprio: o que se renomeia é EXATAMENTE o que a busca enxerga como etiqueta. Cerca de código, código
/// em linha, "C#" no meio da frase, "\#escapado" — tudo que o analisador pula, isto pula, porque é o
/// mesmo código pulando.
///
/// NO FRONTMATTER, A LISTA É REESCRITA E DEDUPLICADA: mesclar "#pegadinh" em "#pegadinha" numa nota que
/// já tinha as duas não pode deixar "tags: [pegadinha, pegadinha]" — a sujeira ficaria visível no
/// Obsidian, que lê o mesmo arquivo.
///
/// A CLASSE É PURA: texto entra, texto sai. Quem lê nota, grava e reindexa é a aplicação.
/// </summary>
public static class RenomeadorDeEtiqueta
{
    /// <summary>
    /// O conteúdo com a etiqueta renomeada. Devolve A MESMA instância quando nada mudou — é como o
    /// chamador sabe que não precisa gravar.
    /// </summary>
    public static string Renomear(string conteudo, Etiqueta de, Etiqueta para)
    {
        ArgumentNullException.ThrowIfNull(de);
        ArgumentNullException.ThrowIfNull(para);
        if (string.IsNullOrEmpty(conteudo) || de == para) return conteudo;

        var texto = RenomearNoFrontmatter(conteudo, de, para);
        texto = RenomearNoTexto(texto, de, para);

        // Instância original quando o resultado é igual: a comparação char a char custa menos que uma
        // gravação à toa — e o histórico não ganha uma revisão idêntica.
        return string.Equals(texto, conteudo, StringComparison.Ordinal) ? conteudo : texto;
    }

    /// <summary>"#de" vira "#para"; "#de/filho" vira "#para/filho"; o resto fica como está.</summary>
    private static Etiqueta? Renomeada(Etiqueta atual, Etiqueta de, Etiqueta para) =>
        atual == de ? para
        : atual.EstaAbaixoDe(de) ? Etiqueta.TentarCriar(para.Valor + atual.Valor[de.Valor.Length..])
        : null;

    private static string RenomearNoFrontmatter(string conteudo, Etiqueta de, Etiqueta para)
    {
        var frontmatter = Frontmatter.Ler(conteudo.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        var texto = conteudo;

        foreach (var chave in new[] { "tags", "tag" })
        {
            var atuais = frontmatter.Lista(chave);
            if (atuais.Count == 0) continue;

            var mudou = false;
            var novas = new List<string>();
            foreach (var bruta in atuais)
            {
                var etiqueta = Etiqueta.TentarCriar(bruta);
                var nova = etiqueta is null ? null : Renomeada(etiqueta, de, para);
                var valor = nova?.Valor ?? etiqueta?.Valor ?? bruta;
                if (nova is not null) mudou = true;
                if (!novas.Contains(valor)) novas.Add(valor);   // a deduplicação da mesclagem
            }

            if (mudou) texto = EditorDeFrontmatter.DefinirLista(texto, chave, novas);
        }

        return texto;
    }

    private static string RenomearNoTexto(string conteudo, Etiqueta de, Etiqueta para)
    {
        var linhas = conteudo.Split('\n');
        var frontmatter = Frontmatter.Ler(conteudo.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));

        string? cercaAberta = null;
        var mudouAlgo = false;

        for (var i = frontmatter.LinhasOcupadas; i < linhas.Length; i++)
        {
            var linha = linhas[i];
            var semIndentacao = linha.TrimStart();
            var cerca = AnalisadorDeNota.DetectarCerca(semIndentacao);
            if (cercaAberta is not null)
            {
                if (cerca is not null && cerca == cercaAberta) cercaAberta = null;
                continue;
            }
            if (cerca is not null) { cercaAberta = cerca; continue; }

            // DE TRÁS PARA FRENTE dentro da linha: substituir muda os índices de tudo que vem depois —
            // na ordem inversa, cada posição ainda vale quando chega a vez dela.
            var trocas = new List<(int Inicio, int Comprimento, string Nova)>();
            foreach (var (inicio, fim) in AnalisadorDeNota.TrechosForaDeCodigo(linha))
            {
                foreach (var achada in AnalisadorDeNota.EtiquetasPosicionadas(linha[inicio..fim]))
                {
                    if (Renomeada(achada.Etiqueta, de, para) is { } nova)
                        trocas.Add((inicio + achada.Inicio, achada.Comprimento, "#" + nova.Valor));
                }
            }
            if (trocas.Count == 0) continue;

            var editada = linha;
            foreach (var (inicio, comprimento, nova) in trocas.OrderByDescending(t => t.Inicio))
                editada = editada[..inicio] + nova + editada[(inicio + comprimento)..];

            linhas[i] = editada;
            mudouAlgo = true;
        }

        return mudouAlgo ? string.Join('\n', linhas) : conteudo;
    }
}
