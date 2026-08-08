using System.Globalization;
using System.Text;

namespace Dominica.Learn.Domain.Templates;

/// <summary>Contexto para preencher um template. Tudo que o domínio precisa saber para substituir.</summary>
public sealed record ContextoDoTemplate(string Titulo, DateTimeOffset Agora, string? Autor = null);

/// <summary>
/// Preenche um template de nota.
///
/// TEMPLATE É UMA NOTA, numa pasta do vault. Não é uma tabela, não é um arquivo de configuração da
/// aplicação. Isso decorre da mesma regra de sempre: template é conhecimento do usuário — ele escreveu
/// aquele roteiro de resumo, aquele formato de ficha de questão — e conhecimento do usuário mora no
/// vault, onde sobrevive a um reindexar e onde ele pode editar pelo Obsidian, no celular, onde quiser.
///
/// SINTAXE: {{titulo}}, {{data}}, {{hora}}, {{data:FORMATO}}, {{autor}}. Deliberadamente pequena — a do
/// Obsidian é essa. Um template com lógica (condicional, laço) deixa de ser template e vira programa, e
/// aí o usuário está depurando chaves duplas às onze da noite em vez de estudar.
///
/// Variável desconhecida é DEIXADA COMO ESTÁ, não apagada: se alguém escreveu {{quest}} por engano, ver
/// {{quest}} no texto conta o que aconteceu; ver um buraco não conta nada.
/// </summary>
public static class AplicadorDeTemplate
{
    public static string Aplicar(string template, ContextoDoTemplate contexto)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var sb = new StringBuilder(template.Length + 64);
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                var fim = template.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (fim > 0)
                {
                    var expressao = template[(i + 2)..fim].Trim();
                    var valor = Resolver(expressao, contexto);
                    // null = não reconheci: devolve o texto original, incluindo as chaves
                    sb.Append(valor ?? template[i..(fim + 2)]);
                    i = fim + 2;
                    continue;
                }
            }
            sb.Append(template[i]);
            i++;
        }
        return sb.ToString();
    }

    private static string? Resolver(string expressao, ContextoDoTemplate c)
    {
        var doisPontos = expressao.IndexOf(':');
        var nome = (doisPontos < 0 ? expressao : expressao[..doisPontos]).Trim().ToLowerInvariant();
        var formato = doisPontos < 0 ? null : expressao[(doisPontos + 1)..].Trim();

        // O horário é LOCAL, e é intencional: quem escreve "criado em {{hora}}" quer a hora do relógio
        // dele, não UTC. O armazenamento continua em UTC; só a apresentação converte.
        var local = c.Agora;

        return nome switch
        {
            "titulo" or "title" => c.Titulo,
            "autor" or "author" => c.Autor ?? string.Empty,
            "data" or "date" => local.ToString(formato ?? "yyyy-MM-dd", CultureInfo.InvariantCulture),
            "hora" or "time" => local.ToString(formato ?? "HH:mm", CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    /// <summary>As variáveis que existem, para a interface poder mostrar sem ninguém decorar.</summary>
    public static IReadOnlyList<(string Variavel, string Explicacao)> Variaveis =>
    [
        ("{{titulo}}", "o nome da nota que está sendo criada"),
        ("{{data}}", "a data de hoje (aceita formato: {{data:dd/MM/yyyy}})"),
        ("{{hora}}", "a hora agora (aceita formato: {{hora:HH'h'mm}})"),
        ("{{autor}}", "quem está criando a nota"),
    ];
}
