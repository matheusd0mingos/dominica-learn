using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Web.Data;

/// <summary>
/// A linha que diz "esta nota está publicada neste endereço".
///
/// MORA NO BANCO DA IDENTIDADE, junto do acompanhamento, e não no índice do vault. O índice é
/// reconstruível — a documentação manda reindexar do zero quando algo está estranho — e uma reindexação
/// que levasse os links junto mataria, de uma vez, todas as URLs que as pessoas já mandaram por aí. O
/// caminho de volta não existe: um índice recriado a partir do disco não sabe qual token era o de cada
/// nota, e "recriar" aqui significaria sortear outros.
///
/// O TOKEN É A CHAVE PRIMÁRIA. Ele já é único, já é o que vem na URL e já é o que se procura: um id
/// numérico ao lado criaria uma segunda maneira de apontar para a mesma publicação, e a segunda existiria
/// só para divergir da primeira.
/// </summary>
[PrimaryKey(nameof(Token))]
public class LinkDeNotaNoBanco
{
    /// <summary>O segredo. Ver <c>TokenDeLink</c>: aqui ele vale como senha, não como identificador.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Quem publicou — e de quem é o vault que a rota anônima vai ler.</summary>
    public string Dono { get; set; } = string.Empty;

    /// <summary>Qual vault dele. Sem esta coluna a leitura cairia no vault padrão e entregaria a nota errada — calada, e plausível.</summary>
    public string Vault { get; set; } = string.Empty;

    /// <summary>O caminho da nota dentro do vault, como está no disco.</summary>
    public string Caminho { get; set; } = string.Empty;

    /// <summary>Quando foi publicada. É o que a lista "o que eu abri para fora" ordena, e a pergunta que ela responde é "desde quando isto está no ar?".</summary>
    public DateTimeOffset CriadoEm { get; set; }
}
