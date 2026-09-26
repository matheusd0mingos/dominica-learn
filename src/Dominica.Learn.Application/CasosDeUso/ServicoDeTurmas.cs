using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Compartilhamento;
using Dominica.Learn.Domain.Vault;
using Microsoft.Extensions.Logging;

namespace Dominica.Learn.Application.CasosDeUso;

/// <summary>
/// Turmas: o jeito de um professor não depender de trinta pessoas lembrarem do apelido dele.
///
/// ENTRAR NUMA TURMA É UM ATO DO ALUNO, e o que ele grava é um <see cref="Acompanhamento"/> comum — o
/// mesmo que ele criaria à mão no painel, e que ele revoga no mesmo lugar. Esta classe NUNCA concede
/// acesso em nome de terceiros: ela chama o <see cref="ServicoDeAcompanhamento"/>, que por sua vez toma
/// o dono de quem está logado. Se um dia isto aqui puder conceder por conta própria, a turma terá
/// virado uma autoridade — e alguém entrará numa turma alheia arrastando o painel de outra pessoa.
/// </summary>
public sealed class ServicoDeTurmas(
    ITurmas turmas,
    ServicoDeAcompanhamento acompanhamento,
    IUsuarioAtual usuario,
    ILogger<ServicoDeTurmas> log)
{
    /// <summary>Cria uma turma minha. O código é sorteado; o dono é sempre quem está logado.</summary>
    public async Task<Resultado<Turma>> CriarAsync(string? nome, CancellationToken ct = default)
    {
        var dono = await usuario.ApelidoAsync(ct);

        // TENTA ALGUMAS VEZES por causa da colisão de código. Com 31^8 combinações ela é remota, mas
        // "remota" não é "impossível" — e o modo de falhar sem esta laçada seria criar uma turma que
        // rouba o código de outra, mandando os alunos dela para o professor errado.
        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            var codigo = CodigoDeTurma.Sortear();
            if (await turmas.PorCodigoAsync(codigo, ct) is not null) continue;

            var turma = Turma.TentarCriar(codigo, dono, nome);
            if (turma is null)
                return Resultado<Turma>.Invalida(
                    $"Dê um nome à turma (até {Turma.TamanhoMaximoDoNome} caracteres).");

            await turmas.CriarAsync(turma, ct);
            log.LogInformation("{Dono} criou a turma {Codigo}", dono.Valor, codigo.Valor);
            return Resultado<Turma>.Sucesso(turma);
        }

        return Resultado<Turma>.Invalida("Não consegui gerar um código livre agora. Tente de novo.");
    }

    public async Task<IReadOnlyList<Turma>> MinhasAsync(CancellationToken ct = default) =>
        await turmas.DeQuemAsync(await usuario.ApelidoAsync(ct), ct);

    public async Task<IReadOnlyList<Turma>> EmQueEstouAsync(CancellationToken ct = default) =>
        await turmas.EmQueEstouAsync(await usuario.ApelidoAsync(ct), ct);

    /// <summary>
    /// A turma daquele código, para a tela PERGUNTAR ANTES de entrar.
    ///
    /// EXISTE POR CAUSA DO GOLPE ÓBVIO: um código é ditado por voz, escrito no quadro, colado num grupo.
    /// Entrar direto significaria abrir o próprio painel para quem quer que tenha inventado aquela
    /// turma. Ver o nome do dono antes de confirmar é o que transforma isso numa decisão.
    /// </summary>
    public async Task<Turma?> ConferirAsync(string? codigoBruto, CancellationToken ct = default) =>
        CodigoDeTurma.TentarCriar(codigoBruto, out var codigo, out _) && codigo is not null
            ? await turmas.PorCodigoAsync(codigo, ct)
            : null;

    /// <summary>
    /// Entra na turma: matrícula + o acompanhamento do MEU vault para o dono dela.
    ///
    /// A ORDEM É MATRÍCULA DEPOIS DO ACESSO, e não o contrário. Se o acesso falhar, não fica uma
    /// matrícula prometendo ao professor um aluno que ele não consegue abrir — a lista dele mostraria o
    /// nome com "sem acesso" e ninguém saberia por quê. O inverso (acesso concedido sem matrícula) é
    /// recuperável e visível: o professor vê o painel, e o aluno vê a concessão no painel dele.
    /// </summary>
    public async Task<Resultado<Turma>> EntrarAsync(string? codigoBruto, CancellationToken ct = default)
    {
        if (!CodigoDeTurma.TentarCriar(codigoBruto, out var codigo, out var erro) || codigo is null)
            return Resultado<Turma>.Invalida(erro ?? "Código inválido.");

        var turma = await turmas.PorCodigoAsync(codigo, ct);
        if (turma is null) return Resultado<Turma>.NaoEncontrada("Essa turma");

        var eu = await usuario.ApelidoAsync(ct);
        if (turma.Dono == eu)
            return Resultado<Turma>.Invalida("Esta turma é sua — você já vê o seu próprio painel.");

        var acesso = await acompanhamento.ConvidarAsync(turma.Dono.Valor, ct);
        if (!acesso.Ok) return Resultado<Turma>.Invalida(acesso.Mensagem ?? "Não consegui abrir o acesso.");

        await turmas.MatricularAsync(codigo, eu, await usuario.VaultAsync(ct), ct);
        log.LogInformation("{Aluno} entrou na turma {Codigo} de {Dono}", eu.Valor, codigo.Valor, turma.Dono.Valor);
        return Resultado<Turma>.Sucesso(turma);
    }

    /// <summary>
    /// Sai da turma E fecha o acesso.
    ///
    /// OS DOIS JUNTOS porque é isso que "sair" significa para quem clica. Tirar só a matrícula deixaria
    /// o professor vendo o painel de alguém que acha que saiu — a pior forma de errar aqui, porque a
    /// pessoa fica tranquila justamente por ter agido.
    /// </summary>
    public async Task<bool> SairAsync(string? codigoBruto, CancellationToken ct = default)
    {
        if (!CodigoDeTurma.TentarCriar(codigoBruto, out var codigo, out _) || codigo is null) return false;

        var turma = await turmas.PorCodigoAsync(codigo, ct);
        if (turma is null) return false;

        await acompanhamento.RevogarAsync(turma.Dono.Valor, ct);
        return await turmas.DesmatricularAsync(codigo, await usuario.ApelidoAsync(ct), ct);
    }

    /// <summary>A lista de alunos — só para o dono da turma.</summary>
    public async Task<IReadOnlyList<AlunoDaTurma>?> AlunosAsync(string? codigoBruto, CancellationToken ct = default)
    {
        if (!CodigoDeTurma.TentarCriar(codigoBruto, out var codigo, out _) || codigo is null) return null;

        var turma = await turmas.PorCodigoAsync(codigo, ct);
        // NÃO É SUA, NÃO É SUA LISTA. A lista diz quem estuda com quem, e isso já é informação sobre
        // gente — mesmo sem abrir painel nenhum.
        if (turma is null || turma.Dono != await usuario.ApelidoAsync(ct)) return null;

        return await turmas.AlunosAsync(codigo, ct);
    }

    /// <summary>
    /// Apaga a turma — e ABRE MÃO do acesso a todos os alunos dela.
    ///
    /// A RENÚNCIA VEM JUNTO porque a alternativa é pior de um jeito silencioso: apagada a turma, o
    /// professor continuaria vendo os painéis, só que sem nenhuma lista onde isso apareça. Nem ele nem
    /// os alunos teriam como notar. "Encerrei a turma" tem de significar "não vejo mais".
    ///
    /// RENUNCIAR É SEMPRE PERMITIDO — é o convidado devolvendo o que recebeu, e nunca uma escalada. Não
    /// é o mesmo que revogar em nome do aluno: se ele voltar a compartilhar, é escolha dele, no painel
    /// dele. E é BEST-EFFORT: falhar em soltar um acesso não pode impedir de apagar a turma, senão a
    /// pessoa fica presa a uma turma que ela quer encerrar.
    /// </summary>
    public async Task<bool> ApagarAsync(string? codigoBruto, CancellationToken ct = default)
    {
        if (!CodigoDeTurma.TentarCriar(codigoBruto, out var codigo, out _) || codigo is null) return false;

        var eu = await usuario.ApelidoAsync(ct);
        foreach (var aluno in await AlunosAsync(codigoBruto, ct) ?? [])
            try { await acompanhamento.RenunciarAsync(aluno.Aluno, aluno.Vault, ct); }
            catch (Exception e) { log.LogWarning(e, "não consegui soltar o acesso a {Aluno}", aluno.Aluno.Valor); }

        return await turmas.ApagarAsync(codigo, eu, ct);
    }
}
