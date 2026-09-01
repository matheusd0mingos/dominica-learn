using Dominica.Learn.Application.Portas;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Web.Seguranca;

/// <summary>
/// Adaptador de <see cref="ILeituraComoOutraPessoa"/>: abre um escopo de injeção NOVO, declara nele
/// que o dono é outra pessoa, e deixa a pilha de sempre trabalhar.
///
/// O ESCOPO NOVO É O PONTO INTEIRO DESTA CLASSE. No Blazor Server o escopo de injeção é o CIRCUITO — a
/// aba aberta. O <see cref="EscopoDoUsuario"/> é registrado como scoped, então declarar o dono no escopo
/// do circuito trocaria a identidade da aba: o professor abriria o painel do aluno e, dali em diante, o
/// próprio "Notas" dele leria o vault do aluno, e o que ele escrevesse seria gravado lá. Nada disso daria
/// erro — daria os dados errados, calados, que é o defeito que este projeto mais teme.
///
/// Um escopo criado aqui e descartado no fim da chamada não tem como escapar: o
/// <see cref="ServicoDeDesempenho"/> resolvido dentro dele morre com ele, e o circuito de quem está
/// olhando nunca soube que existiu.
///
/// NÃO PERGUNTA SE PODE. Quem autoriza é o <see cref="ServicoDeAcompanhamento"/>, e a separação é de
/// propósito: um mecanismo que também autoriza ganha um parâmetro "confia em mim" no primeiro caso
/// especial, e a partir daí a autorização passa a depender de quem chama.
/// </summary>
public sealed class LeituraEmEscopoProprio(IServiceScopeFactory escopos) : ILeituraComoOutraPessoa
{
    public async Task<T> LendoComoAsync<TServico, T>(
        ApelidoDoUsuario dono,
        NomeDoVault vault,
        Func<TServico, Task<T>> leitura,
        CancellationToken ct = default) where TServico : notnull
    {
        using var escopo = escopos.CreateScope();

        // OS DOIS JUNTOS, SEMPRE. Declarar o apelido e esquecer o vault não dá erro: dá lista vazia,
        // porque nenhuma linha real tem vault vazio — e lista vazia lê-se como "essa pessoa não estudou
        // nada", que é uma mentira convincente. É a mesma armadilha anotada no RegistroEmPostgres.
        escopo.ServiceProvider.GetRequiredService<EscopoDoUsuario>().Definir(dono, vault);

        var servico = escopo.ServiceProvider.GetRequiredService<TServico>();
        return await leitura(servico);
    }
}
