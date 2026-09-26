using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Web.Estudo;

/// <summary>
/// O cronômetro de estudo EM CURSO, com escopo de CIRCUITO.
///
/// POR QUE O ESTADO MORA AQUI, e não no componente que o desenha: um cronômetro só serve se continua
/// contando quando a pessoa sai do painel para abrir as notas ou a revisão. O widget que o mostra é
/// recriado a cada página; o circuito do Blazor Server, não — então o tempo vive no serviço do circuito
/// e o widget só lê. É o mesmo motivo de <see cref="Tema.EstadoDoTema"/> ser serviço, e não parâmetro.
///
/// ISTO NÃO É O REGISTRO. O registro é durável (banco) e só recebe a sessão quando a pessoa PARA o
/// cronômetro. Fechar o navegador no meio perde a contagem — como perder o cronômetro de pulso, e pela
/// mesma razão: ninguém prometeu guardá-la antes de você parar.
///
/// O TEMPO SAI DO RELÓGIO (porta), e não de DateTime.Now espalhado: o mesmo relógio que o resto do
/// domínio usa, o que deixa o serviço testável sem esperar o tempo passar.
/// </summary>
public sealed class EstadoDoCronometro(IRelogio relogio)
{
    private DateTimeOffset _inicio;
    private TimeSpan _acumulado;

    /// <summary>Avisa o widget (em qualquer página) que o estado mudou.</summary>
    public event Action? Mudou;

    public Materia? Materia { get; private set; }
    public bool Rodando { get; private set; }

    /// <summary>
    /// O que a pessoa estudou, nas palavras dela — opcional, vai junto quando a sessão é registrada.
    ///
    /// MORA AQUI, e não no widget, pela mesma razão do tempo: o widget é recriado a cada navegação, e
    /// perder o texto digitado por ter aberto uma nota no meio do bloco seria pedir para ninguém usar o
    /// campo. Escrever no meio do estudo é normal; o estado tem que aguentar.
    /// </summary>
    public string Observacao { get; set; } = string.Empty;

    /// <summary>Há um cronômetro na tela — rodando ou pausado com tempo já contado.</summary>
    public bool Ativo => Rodando || _acumulado > TimeSpan.Zero;

    /// <summary>O tempo contado até agora — a fonte da verdade para o que vai virar registro.</summary>
    public TimeSpan Decorrido => _acumulado + (Rodando ? relogio.Agora - _inicio : TimeSpan.Zero);

    public void Iniciar(Materia materia)
    {
        Materia = materia;
        Observacao = string.Empty;   // a descrição é DESTA sessão; a da anterior não pode vazar para cá
        _acumulado = TimeSpan.Zero;
        _inicio = relogio.Agora;
        Rodando = true;
        Mudou?.Invoke();
    }

    public void Pausar()
    {
        if (!Rodando) return;
        _acumulado = Decorrido;   // congela o que já correu antes de parar o relógio
        Rodando = false;
        Mudou?.Invoke();
    }

    public void Retomar()
    {
        if (Rodando || Materia is null) return;
        _inicio = relogio.Agora;
        Rodando = true;
        Mudou?.Invoke();
    }

    /// <summary>Zera e apaga a matéria — depois de registrar, ou ao descartar a contagem.</summary>
    public void Zerar()
    {
        Materia = null;
        Rodando = false;
        _acumulado = TimeSpan.Zero;
        Observacao = string.Empty;
        Mudou?.Invoke();
    }
}
