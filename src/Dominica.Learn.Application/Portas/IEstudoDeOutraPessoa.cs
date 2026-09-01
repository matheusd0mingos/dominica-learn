using Dominica.Learn.Application.CasosDeUso;
using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Application.Portas;

/// <summary>
/// LER O REGISTRO DE OUTRA PESSOA — o único caminho sancionado para atravessar a fronteira.
///
/// A FRONTEIRA NÃO É FURADA, ELA É REDECLARADA. O registro filtra por <c>(Usuario, Vault)</c> num filtro
/// global do EF, alimentado pelo <see cref="IUsuarioAtual"/>. A tentação, ao precisar do painel de
/// outra pessoa, é escrever uma consulta que ignora o filtro — e é exatamente aí que nasce o vazamento
/// que ninguém consegue depurar, porque a consulta sem filtro parece igual à consulta com filtro.
///
/// Aqui não se ignora nada: abre-se um escopo NOVO declarando que o dono é outro, e a mesma pilha de
/// sempre — mesmo serviço, mesmo repositório, mesmo filtro — trabalha por ele. Nenhuma consulta nova é
/// escrita, então nenhuma consulta nova pode esquecer o filtro.
///
/// POR QUE UM ESCOPO NOVO, E NÃO O <see cref="EscopoDoUsuario"/> DO CIRCUITO: no Blazor Server o escopo
/// de injeção é o CIRCUITO inteiro. Declarar o dono ali trocaria a identidade da aba — o professor
/// abriria o painel do aluno e, a partir dali, o seu próprio "Notas" mostraria o vault do aluno, e o que
/// ele escrevesse seria gravado lá. Um escopo próprio, criado e descartado nesta chamada, é o que impede
/// que a leitura de um contamine a sessão do outro.
///
/// A AUTORIZAÇÃO NÃO MORA AQUI. Esta porta é o mecanismo; quem decide se pode é o
/// <see cref="ServicoDeAcompanhamento"/>. Separados de propósito: um mecanismo que também autoriza
/// tende a ganhar um parâmetro "confia em mim" no primeiro caso especial.
/// </summary>
public interface IEstudoDeOutraPessoa
{
    /// <summary>
    /// Roda <paramref name="leitura"/> com o serviço de desempenho apontado para <paramref name="dono"/>
    /// no vault <paramref name="vault"/>.
    ///
    /// A ASSINATURA SÓ DEIXA LER porque o que ela entrega é o serviço, e é o chamador que escolhe o que
    /// perguntar. Devolver "um ServicoDeDesempenho do fulano" para o chamador guardar seria pior: ele
    /// sobreviveria ao escopo, e um serviço vivo depois do escopo morto é um objeto que lê de um
    /// contexto descartado — ou, pior, de um contexto reaproveitado por outra pessoa.
    /// </summary>
    Task<T> LendoComoAsync<T>(
        ApelidoDoUsuario dono,
        NomeDoVault vault,
        Func<ServicoDeDesempenho, Task<T>> leitura,
        CancellationToken ct = default);
}
