using System.Security.Cryptography;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Domain.Compartilhamento;

/// <summary>
/// O endereço secreto de uma nota publicada.
///
/// AQUI O TOKEN É A AUTORIZAÇÃO — não há login do outro lado, e é essa diferença que muda tudo em
/// relação ao <see cref="CodigoDeTurma"/>. Lá o código é endereço: quem adivinha um não lê nada, apenas
/// ganha a chance de OFERECER o próprio painel. Aqui, quem adivinha um token LÊ a nota. Daí as três
/// consequências que este tipo existe para garantir:
///
///   • SORTEADO POR CRIPTOGRAFIA, e com folga. São <see cref="Tamanho"/> caracteres num alfabeto de 62
///     — mais de 120 bits. <c>Guid.NewGuid()</c> teria sido o reflexo, e é a armadilha clássica: 122
///     bits no papel, mas gerado por um algoritmo que não promete imprevisibilidade, e com pedaços
///     estruturados. Para identificar linha, Guid serve; para valer como senha, não.
///
///   • FORMATO CONFERIDO ANTES DO BANCO. O <see cref="TentarCriar"/> recusa qualquer coisa fora do
///     alfabeto e do tamanho — então <c>/n/&lt;lixo&gt;</c> é 404 sem uma consulta sequer. Sem isso, a
///     rota pública vira um martelo de banco de graça para qualquer varredor.
///
///   • DESCARTÁVEL. O token não é derivado do caminho nem do dono: é sorteado e guardado. É o que
///     permite revogar — apagada a linha, a URL morre — e o que impede que republicar a mesma nota
///     ressuscite um endereço que alguém achou que tinha matado.
///
/// O ALFABETO NÃO OMITE LETRAS PARECIDAS, ao contrário do código de turma, porque este aqui nunca é
/// ditado nem copiado à mão: ele viaja num link colado. Tirar caracteres custaria entropia para resolver
/// um problema que não existe neste uso.
/// </summary>
public sealed record TokenDeLink
{
    private const string Alfabeto = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>22 caracteres de 62 ≈ 131 bits. Adivinhar não é uma ameaça a considerar.</summary>
    public const int Tamanho = 22;

    public string Valor { get; }

    private TokenDeLink(string valor) => Valor = valor;

    public static TokenDeLink Sortear() =>
        new(RandomNumberGenerator.GetString(Alfabeto, Tamanho));

    /// <summary>
    /// O token que veio da URL. NÃO normaliza nada — nem espaço, nem caixa.
    ///
    /// É o oposto de propósito do <see cref="CodigoDeTurma.TentarCriar"/>, que aceita "abcd-2345" porque
    /// alguém copiou do quadro. Aqui, aceitar variação seria colapsar tokens diferentes num só: "aB…" e
    /// "Ab…" são dois segredos distintos, e tratá-los como o mesmo dividiria o espaço de busca — sem
    /// dar erro, e sem que ninguém percebesse.
    /// </summary>
    public static bool TentarCriar(string? bruto, out TokenDeLink? token)
    {
        token = null;
        if (bruto is null || bruto.Length != Tamanho) return false;
        foreach (var c in bruto) if (!Alfabeto.Contains(c, StringComparison.Ordinal)) return false;

        token = new TokenDeLink(bruto);
        return true;
    }

    public override string ToString() => Valor;
}

/// <summary>
/// Uma nota publicada: <c>{Dono}</c> abriu <c>{Caminho}</c> do vault <c>{Vault}</c> para quem tiver o
/// <see cref="Token"/>.
///
/// É DE UMA NOTA, E NUNCA DE UMA PASTA. Publicar uma matéria inteira pareceria mais útil e seria a pior
/// regra possível: a nota escrita amanhã nasceria pública, sem ninguém ter decidido isso. Quem publica
/// escolhe um arquivo de cada vez, e o que ele publicou é o que ele vê na lista.
///
/// O CAMINHO É PARTE DA LINHA, e não uma referência que se resolve depois: renomear a nota não deve
/// mudar o que o link entrega em silêncio. Se o caminho deixar de existir, o link responde "não está
/// mais aqui" — que é a verdade — em vez de apontar para o arquivo que hoje ocupa aquele nome.
///
/// SÓ LEITURA. Não há papel, não há "editor": editar por link anônimo é outro problema, e um que não se
/// resolve acrescentando um enum aqui — precisa de permissão no caminho de ESCRITA, de autoria (quem
/// escreveu, se não há quem?) e de conflito. Um campo <c>Papel</c> nesta linha faria parecer que basta
/// mudar o valor, e é assim que se publica um vault inteiro para escrita achando que se mexeu num rótulo.
/// </summary>
public sealed record LinkDeNota
{
    public TokenDeLink Token { get; }
    public ApelidoDoUsuario Dono { get; }
    public NomeDoVault Vault { get; }
    public CaminhoNota Caminho { get; }
    public DateTimeOffset CriadoEm { get; }

    private LinkDeNota(TokenDeLink token, ApelidoDoUsuario dono, NomeDoVault vault, CaminhoNota caminho, DateTimeOffset criadoEm)
    {
        Token = token;
        Dono = dono;
        Vault = vault;
        Caminho = caminho;
        CriadoEm = criadoEm;
    }

    /// <summary>Devolve null quando falta peça. Um link sem dono ou sem caminho não é link ruim: é linha que autoriza o nada, e que uma consulta descuidada leria como "autoriza tudo".</summary>
    public static LinkDeNota? TentarCriar(
        TokenDeLink? token, ApelidoDoUsuario? dono, NomeDoVault? vault, CaminhoNota? caminho, DateTimeOffset criadoEm)
    {
        if (token is null || dono is null || vault is null || caminho is null) return null;
        return new LinkDeNota(token, dono, vault, caminho, criadoEm);
    }

    /// <summary>O endereço relativo, do jeito que a aplicação o serve. Relativo porque o Learn vive num sub-caminho — ver OpcoesDeHospedagem.</summary>
    public string CaminhoNaUrl => $"n/{Token.Valor}";
}
