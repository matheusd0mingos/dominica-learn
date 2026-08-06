namespace Dominica.Learn.Web.Tema;

/// <summary>
/// Claro ou escuro, para quem precisa DESENHAR e não só herdar estilo.
///
/// O MudBlazor troca as variáveis CSS quando o tema muda, mas não deixa marca nenhuma no HTML — nem
/// classe, nem atributo. Isso basta para tudo que é estilizado por ele e não basta para o grafo, que
/// escolhe as cores das matérias em C#: a paleta tem um passo para fundo claro e outro para fundo escuro,
/// validados separadamente, e usar o do tema errado quebra o contraste que o validador aprovou.
///
/// Serviço com escopo de circuito, e não parâmetro em cascata, porque quem alterna (o componente de
/// provedores) e quem desenha (a página do grafo) são irmãos na árvore, não pai e filho.
/// </summary>
public sealed class EstadoDoTema
{
    private bool _escuro;

    /// <summary>Avisa quem estiver desenhando que as cores mudaram.</summary>
    public event Action? Mudou;

    public bool Escuro
    {
        get => _escuro;
        set
        {
            if (_escuro == value) return;
            _escuro = value;
            Mudou?.Invoke();
        }
    }
}
