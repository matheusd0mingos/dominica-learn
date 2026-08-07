using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Compartilhamento;

/// <summary>O que se pode fazer com um caminho. A ordem importa: cada nível contém o anterior.</summary>
public enum Acesso
{
    Negado,
    Leitura,
    Escrita,
}

/// <summary>
/// A DECISÃO DE ACESSO. Dado quem eu sou, de quem é o vault, qual caminho eu quero e quais concessões
/// existem — posso ler? posso escrever?
///
/// POR QUE ELA É PURA, e por que isso não é preferência de estilo:
///
/// Esta é a única classe do sistema cujo defeito entrega o conhecimento de uma pessoa a outra sem dar
/// erro nenhum. Sendo função de texto para enum, cada forma de burlá-la se escreve num teste de três
/// linhas — sem Postgres, sem disco, sem sessão. O teste que não custa nada para escrever é o teste que
/// alguém escreve quando muda a regra daqui a cinco anos.
///
/// O QUE ELA NÃO FAZ, de propósito: ela não sabe onde ficam os arquivos. Quem traduz "pode ler o vault do
/// Bruno" em um caminho de disco é a infraestrutura, e ela só o faz depois de perguntar aqui. Assim, um
/// código que esquecesse de perguntar não teria pasta nenhuma para abrir — que é a mesma proteção que já
/// vale para o vault próprio.
/// </summary>
public static class PermissaoNoVault
{
    /// <summary>
    /// <paramref name="concessoes"/> é a lista de concessões conhecidas — normalmente as que têm
    /// <paramref name="quem"/> como convidado. Concessões de terceiros no meio da lista não fazem mal:
    /// elas simplesmente não casam.
    /// </summary>
    public static Acesso Para(
        ApelidoDoUsuario? quem,
        ApelidoDoUsuario? dono,
        CaminhoNota? caminho,
        IEnumerable<Concessao>? concessoes)
    {
        // Falta de informação é NEGADO, nunca "assume o caso comum". Um nulo aqui significa que alguém
        // chamou esta função sem saber quem está pedindo — e a resposta certa para "não sei quem é você"
        // nunca foi "pode entrar".
        if (quem is null || dono is null || caminho is null) return Acesso.Negado;

        // O dono manda no vault dele, inteiro. Não é caso especial: é a regra que já vale hoje, dita aqui
        // para que esta função responda sozinha e ninguém precise combinar duas respostas.
        if (quem == dono) return Acesso.Escrita;

        if (concessoes is null) return Acesso.Negado;

        var melhor = Acesso.Negado;
        foreach (var concessao in concessoes)
        {
            if (concessao is null) continue;
            if (concessao.Convidado != quem) continue;
            if (concessao.Dono != dono) continue;
            if (!concessao.Alcanca(caminho)) continue;

            // MAIS PERMISSIVA VENCE, e isso precisa estar dito: duas concessões para a mesma pasta — uma
            // de leitor, outra de editor — só aparecem por engano de quem concedeu, e nesse engano a
            // intenção mais recente foi promover. Negar seria a escolha "segura" e produziria o suporte
            // impossível de explicar: "eu te dei acesso de editor e continua sem funcionar".
            var deste = concessao.Papel == PapelNoCompartilhamento.Editor ? Acesso.Escrita : Acesso.Leitura;
            if (deste > melhor) melhor = deste;
        }

        return melhor;
    }

    /// <summary>Atalho legível para o caminho de escrita, que é onde o erro custa caro.</summary>
    public static bool PodeEscrever(
        ApelidoDoUsuario? quem, ApelidoDoUsuario? dono, CaminhoNota? caminho, IEnumerable<Concessao>? concessoes) =>
        Para(quem, dono, caminho, concessoes) == Acesso.Escrita;

    /// <summary>Atalho legível para o caminho de leitura.</summary>
    public static bool PodeLer(
        ApelidoDoUsuario? quem, ApelidoDoUsuario? dono, CaminhoNota? caminho, IEnumerable<Concessao>? concessoes) =>
        Para(quem, dono, caminho, concessoes) >= Acesso.Leitura;
}
