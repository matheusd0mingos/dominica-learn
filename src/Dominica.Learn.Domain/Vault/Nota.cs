using Dominica.Learn.Domain.Analise;

namespace Dominica.Learn.Domain.Vault;

/// <summary>
/// Uma nota do vault: o arquivo .md, com o que se extraiu do conteúdo dele.
///
/// A análise é calculada NA CONSTRUÇÃO e guardada junto. Não é cache: é invariante. Uma Nota cujo
/// conteúdo diga uma coisa e cuja análise diga outra é um objeto inconsistente, e a única forma de
/// garantir que isso nunca aconteça é não permitir alterar o conteúdo — <see cref="ComConteudo"/>
/// devolve uma nota NOVA. Imutabilidade aqui não é preferência estética; é o que dispensa qualquer
/// código futuro de lembrar de reanalisar.
/// </summary>
public sealed class Nota
{
    public CaminhoNota Caminho { get; }
    public string Conteudo { get; }
    public ImpressaoDigital Impressao { get; }
    public DateTimeOffset ModificadoEm { get; }
    public AnaliseDaNota Analise { get; }

    private Nota(CaminhoNota caminho, string conteudo, DateTimeOffset modificadoEm)
    {
        Caminho = caminho;
        Conteudo = conteudo ?? string.Empty;
        Impressao = ImpressaoDigital.De(Conteudo);
        ModificadoEm = modificadoEm;
        Analise = AnalisadorDeNota.Analisar(Conteudo, caminho.Nome);
    }

    public static Nota Criar(CaminhoNota caminho, string conteudo, DateTimeOffset modificadoEm) =>
        new(caminho, conteudo, modificadoEm);

    /// <summary>Mesma nota com outro conteúdo — reanalisada, por construção.</summary>
    public Nota ComConteudo(string novoConteudo, DateTimeOffset modificadoEm) =>
        new(Caminho, novoConteudo, modificadoEm);

    /// <summary>Mesma nota em outro caminho. O título pode mudar, porque o nome do arquivo é a última
    /// palavra sobre o título quando não há frontmatter nem H1 — por isso reconstrói, não copia.</summary>
    public Nota Em(CaminhoNota novoCaminho, DateTimeOffset modificadoEm) =>
        new(novoCaminho, Conteudo, modificadoEm);

    public string Titulo => Analise.Titulo.Length > 0 ? Analise.Titulo : Caminho.Nome;

    public EstadoResumido Estado() => new(Caminho, ModificadoEm, Impressao);

    /// <summary>Projeção leve para reconciliação e listagens — sem carregar o conteúdo.</summary>
    public sealed record EstadoResumido(CaminhoNota Caminho, DateTimeOffset ModificadoEm, ImpressaoDigital Impressao);
}
