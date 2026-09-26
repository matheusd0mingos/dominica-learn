using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

/// <summary>O que a página pública mostra. Já vem renderizado: do outro lado não há circuito para renderizar nada.</summary>
/// <param name="Html">A nota em HTML, saída do mesmo renderizador (e do mesmo saneamento) da leitura de sempre.</param>
public sealed record NotaPublicada(string Titulo, string Html, ApelidoDoUsuario Dono, DateTimeOffset PublicadaEm);

/// <summary>
/// Publicar uma nota num link, e abrir o link do outro lado.
///
/// SÃO DOIS MUNDOS DENTRO DE UMA CLASSE SÓ, e é para isso que ela existe. Um lado (publicar, listar,
/// revogar) é de quem está logado, e o dono vem SEMPRE do <see cref="IUsuarioAtual"/> — nunca de um
/// parâmetro, senão publicar nota alheia seria uma questão de digitar outro apelido. O outro lado
/// (<see cref="AbrirAsync"/>) é anônimo: não há usuário para perguntar, e por isso ele não pode nem
/// encostar no <see cref="IUsuarioAtual"/> — perguntar quem é, ali, é uma exceção em produção na primeira
/// visita de fora.
///
/// A AUTORIZAÇÃO DO LADO ANÔNIMO É O PRÓPRIO TOKEN, e é ele quem diz de quem é o vault a ler. Isso torna
/// a leitura cruzada obrigatória — o visitante não é dono de nada — e ela passa, como a do painel, pelo
/// <see cref="ILeituraComoOutraPessoa"/>: escopo novo declarando o dono, mesma pilha filtrada de sempre.
/// Nenhuma consulta sem filtro é escrita aqui, e é essa ausência que impede o vazamento.
///
/// O QUE O VISITANTE VÊ É SÓ ESTA NOTA. Ver <see cref="RenderizarSozinha"/>: os wikilinks e os anexos
/// não são resolvidos, de propósito.
/// </summary>
public sealed class ServicoDeLinksDeNota(
    ILinksDeNota links,
    IRepositorioDeNotas minhasNotas,
    IRenderizadorDeMarkdown renderizador,
    ILeituraComoOutraPessoa leituraAlheia,
    IUsuarioAtual usuario,
    IRelogio relogio,
    ILogger<ServicoDeLinksDeNota> log)
{
    /// <summary>
    /// Publica a nota do vault em que estou. Devolve o link — o mesmo, se ela já estava publicada.
    ///
    /// CONFERE QUE A NOTA EXISTE antes de gravar. Publicar um caminho inexistente criaria um link que
    /// nasce quebrado: quem o recebesse veria "não está mais aqui" e concluiria que a nota foi apagada,
    /// enquanto quem publicou juraria ter mandado o endereço certo.
    /// </summary>
    public async Task<Resultado<LinkDeNota>> PublicarAsync(string? caminhoBruto, CancellationToken ct = default)
    {
        if (!CaminhoNota.TentarCriar(caminhoBruto, out var caminho, out var erro) || caminho is null)
            return Resultado<LinkDeNota>.Invalida(erro ?? "Caminho inválido.");

        if (!await minhasNotas.ExisteAsync(caminho, ct))
            return Resultado<LinkDeNota>.NaoEncontrada($"A nota \"{caminho}\"");

        var dono = await usuario.ApelidoAsync(ct);
        var vault = await usuario.VaultAsync(ct);

        var link = LinkDeNota.TentarCriar(TokenDeLink.Sortear(), dono, vault, caminho, relogio.Agora);
        if (link is null) return Resultado<LinkDeNota>.Invalida("Não consegui montar o link.");

        // O adaptador é idempotente por (dono, vault, caminho) — o token sorteado acima é descartado se
        // já houver um link. Ver ILinksDeNota.PublicarAsync: dois tokens vivos para a mesma nota fariam
        // "revogar" matar um só, e quem clicasse sairia certo de que fechou o acesso.
        var gravado = await links.PublicarAsync(link, ct);
        log.LogInformation("{Dono} publicou {Caminho} do vault {Vault}", dono.Valor, caminho.Valor, vault.Valor);
        return Resultado<LinkDeNota>.Sucesso(gravado);
    }

    /// <summary>O link desta nota, se houver — para a tela oferecer "copiar" em vez de "publicar".</summary>
    public async Task<LinkDeNota?> DaNotaAsync(CaminhoNota caminho, CancellationToken ct = default) =>
        await links.DaNotaAsync(await usuario.ApelidoAsync(ct), await usuario.VaultAsync(ct), caminho, ct);

    /// <summary>Tudo que EU publiquei, de todos os meus vaults. Sem esta lista, ninguém revoga o que esqueceu que abriu.</summary>
    public async Task<IReadOnlyList<LinkDeNota>> MeusAsync(CancellationToken ct = default) =>
        await links.MeusAsync(await usuario.ApelidoAsync(ct), ct);

    /// <summary>
    /// Mata o link. O dono é quem está logado — revogar é do dono, e receber o dono de fora seria deixar
    /// qualquer um despublicar a nota de qualquer outro.
    /// </summary>
    public async Task<bool> RevogarAsync(string? tokenBruto, CancellationToken ct = default)
    {
        if (!TokenDeLink.TentarCriar(tokenBruto, out var token) || token is null) return false;

        var dono = await usuario.ApelidoAsync(ct);
        var revogou = await links.RevogarAsync(dono, token, ct);
        if (revogou) log.LogInformation("{Dono} revogou um link de nota", dono.Valor);
        return revogou;
    }

    /// <summary>
    /// A nota que está atrás de um token — o lado anônimo.
    ///
    /// NÃO PERGUNTA QUEM ESTÁ VENDO, e não pode: não há ninguém logado. Quem autoriza é o token, e a
    /// linha dele é quem diz de qual vault ler. Null em toda recusa — token malformado, token que não
    /// existe, nota que sumiu do disco — e um null só, sem distinguir os casos: dizer "esse token existe,
    /// mas a nota sumiu" contaria a um varredor que ele acertou o segredo.
    /// </summary>
    public async Task<NotaPublicada?> AbrirAsync(string? tokenBruto, CancellationToken ct = default)
    {
        // O FORMATO ANTES DO BANCO. Ver TokenDeLink: sem esta guarda, /n/<qualquer coisa> vira uma
        // consulta, e a rota pública passa a ser um martelo de banco de graça.
        if (!TokenDeLink.TentarCriar(tokenBruto, out var token) || token is null) return null;

        var link = await links.PorTokenAsync(token, ct);
        if (link is null) return null;

        // A LEITURA É NO VAULT DE OUTRA PESSOA — sempre, porque quem visita não é dono de nada. Passa
        // pelo mecanismo de escopo próprio, o mesmo do painel: nenhuma consulta nova, nenhum filtro para
        // esquecer. Ver ILeituraComoOutraPessoa.
        var nota = await leituraAlheia.LendoComoAsync<IRepositorioDeNotas, Nota?>(
            link.Dono, link.Vault, repo => repo.LerAsync(link.Caminho, ct), ct);

        if (nota is null)
        {
            log.LogInformation("Link válido para uma nota que não está mais no disco: {Caminho}", link.Caminho.Valor);
            return null;
        }

        return new NotaPublicada(nota.Titulo, RenderizarSozinha(nota.Conteudo), link.Dono, link.CriadoEm);
    }

    /// <summary>
    /// Renderiza a nota SEM o vault em volta: wikilink não vira link, <c>![[anexo]]</c> não vira imagem.
    ///
    /// NÃO É ECONOMIA, É O RECORTE DO QUE FOI PUBLICADO. Resolver os wikilinks encheria a página de links
    /// para <c>notas/…</c>, que é rota autenticada: o visitante clicaria e cairia numa tela de login,
    /// concluindo que o link "não abriu". E resolver os anexos apontaria para <c>anexos/…</c>, igualmente
    /// autenticada — imagens quebradas numa página que a pessoa mandou para alguém.
    ///
    /// O QUE SERIA PIOR É A OUTRA SAÍDA: abrir aquelas rotas para o portador do token. Aí publicar uma
    /// nota publicaria, junto, tudo que ela referencia — e as notas que ELAS referenciam. Ninguém escolhe
    /// isso; descobre-se depois. Publica-se um arquivo, e é um arquivo que se lê.
    /// </summary>
    ///
    /// O RESOLVEDOR DE ANEXO VAI NULO, e não como uma função que devolve null: nulo é o jeito de dizer
    /// ao renderizador "aqui não há de onde servir anexo". Passar uma função que sempre nega faria o
    /// renderizador PERGUNTAR por cada `![[…]]` para ouvir "não" toda vez — mesmo resultado na tela, mas
    /// escondendo, de quem lê o código, que a publicação simplesmente não tem armazém de anexos.
    private string RenderizarSozinha(string markdown) =>
        renderizador.Renderizar(markdown, _ => null, anexos: null, notas: null).Html;
}
