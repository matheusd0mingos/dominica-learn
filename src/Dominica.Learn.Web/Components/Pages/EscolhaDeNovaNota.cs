using Dominica.Learn.Domain.Vault;

namespace Dominica.Learn.Web.Components.Pages;

/// <summary>O que o diálogo de criação devolve: onde a nota vai e de qual molde (ou nenhum).</summary>
public sealed record EscolhaDeNovaNota(CaminhoNota Destino, CaminhoNota? Template);
