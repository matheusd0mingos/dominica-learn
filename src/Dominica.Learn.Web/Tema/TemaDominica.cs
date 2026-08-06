using MudBlazor;

namespace Dominica.Learn.Web.Tema;

/// <summary>
/// A identidade da Dominica traduzida para o MudBlazor.
///
/// POR QUE ISTO EXISTE EM C# E NÃO SÓ NO CSS: quase toda a tela do vault é MudBlazor, e o MudBlazor
/// não lê variáveis CSS de fora — ele GERA as dele a partir de um <see cref="MudTheme"/>. Sem este
/// arquivo, os tokens do app.css pintariam a barra e o Identity, e todo o resto do app continuaria
/// com o roxo-e-azul padrão da biblioteca. Foi exatamente essa metade pintada que motivou a tarefa.
///
/// Os valores vêm de novo/plataforma/web/src/styles.css. Nenhum foi inventado aqui.
///
/// A INVERSÃO DA PRIMÁRIA ENTRE OS TEMAS É DELIBERADA. No claro, a primária é o marinho: é a cor da
/// marca e ela tem contraste de sobra sobre branco. No escuro, marinho sobre #16202b seria um botão
/// que ninguém enxerga — lá a primária passa a ser o dourado, que é o que a própria plataforma faz
/// no tema escuro dela (--acento vira #D8B56B). A marca não muda; muda qual das duas cores dela
/// carrega o peso.
/// </summary>
public static class TemaDominica
{
    // Os tokens, com o mesmo nome que têm no styles.css da plataforma.
    public const string Marinho = "#0E1E33";
    public const string Ouro = "#C9A24A";
    public const string OuroClaro = "#D8B56B";
    public const string Creme = "#F7F4EE";

    /// <summary>
    /// A superfície onde o conteúdo — e o GRAFO — é desenhado. Não mude sem reler
    /// <see cref="PaletaDeMaterias"/>: as cores das matérias foram validadas contra estes dois
    /// valores, e trocá-los invalida o contraste que o validador aprovou.
    /// </summary>
    public const string SuperficieClara = "#FFFFFF";

    /// <inheritdoc cref="SuperficieClara"/>
    public const string SuperficieEscura = "#16202b";

    public static MudTheme Criar() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Marinho,
            PrimaryContrastText = "#FFFFFF",   // 16,8:1 sobre o marinho
            Secondary = Ouro,
            SecondaryContrastText = Marinho,   // 8,6:1 — branco sobre dourado daria 2,0:1
            Tertiary = "#2F5D8A",          // o aço do gantt — usado onde marinho e ouro competiriam
            Background = Creme,
            Surface = SuperficieClara,
            AppbarBackground = Marinho,
            AppbarText = "#FFFFFF",
            DrawerBackground = SuperficieClara,
            TextPrimary = "#1B2436",
            TextSecondary = "#6B6A64",
            Divider = "#E6E4DC",
            LinesDefault = "#E6E4DC",
            LinesInputs = "#D8D4C8",
            ActionDefault = "#6B6A64",
            Error = "#A32D2D",
            Success = "#0F6E56",
            Warning = "#B8862F",
            Info = "#2F5D8A",
        },
        PaletteDark = new PaletteDark
        {
            Primary = OuroClaro,           // ver o comentário da classe: no escuro o peso é do ouro
            // O TEXTO SOBRE A PRIMÁRIA DO ESCURO TEM DE SER MARINHO. Sem isto o MudBlazor usa
            // branco, e branco sobre #D8B56B dá 1,96:1 — ilegível. Com o marinho, 8,58:1. Isso
            // vale de uma vez para botão preenchido, chip, aba selecionada e alternador.
            PrimaryContrastText = Marinho,
            Secondary = Ouro,
            SecondaryContrastText = Marinho,
            Tertiary = "#4d7fb0",
            Background = "#0f1720",
            Surface = SuperficieEscura,
            AppbarBackground = "#0A1626",
            AppbarText = "#FFFFFF",
            DrawerBackground = SuperficieEscura,
            TextPrimary = "#ECE7DB",
            TextSecondary = "#b3ab98",
            Divider = "#2a3542",
            LinesDefault = "#2a3542",
            LinesInputs = "#3a4653",
            ActionDefault = "#b3ab98",
            Error = "#e0655a",
            Success = "#3fae8c",
            Warning = "#D8B56B",
            Info = "#4d7fb0",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",  // --raio
        },
        Typography = new Typography
        {
            // O corpo é sem serifa: nota de estudo é texto longo, e ler serifa em tela por hora é
            // cansaço à toa. O serifado da marca fica nos TÍTULOS, logo abaixo.
            Default = new DefaultTypography
            {
                FontFamily = ["system-ui", "-apple-system", "Segoe UI", "Roboto", "Arial", "sans-serif"],
            },
            H1 = new H1Typography { FontFamily = Serifada, FontWeight = "600" },
            H2 = new H2Typography { FontFamily = Serifada, FontWeight = "600" },
            H3 = new H3Typography { FontFamily = Serifada, FontWeight = "600" },
            H4 = new H4Typography { FontFamily = Serifada, FontWeight = "600" },
            H5 = new H5Typography { FontFamily = Serifada, FontWeight = "600" },
            H6 = new H6Typography { FontFamily = Serifada, FontWeight = "600" },
        },
    };

    private static readonly string[] Serifada = ["Georgia", "Times New Roman", "serif"];
}
