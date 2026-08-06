using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Infrastructure.Indice;

/// <summary>Uma nota no índice. Espelha o arquivo; nada aqui é dado original do usuário.</summary>
public sealed class NotaNoIndice
{
    public int Id { get; set; }
    public string Caminho { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Resumo { get; set; } = string.Empty;
    /// <summary>Texto do arquivo, guardado só para a busca. Reconstruível: o .md é que manda.</summary>
    public string Conteudo { get; set; } = string.Empty;
    public string Impressao { get; set; } = string.Empty;
    public DateTimeOffset ModificadoEm { get; set; }
    public int Palavras { get; set; }
    public string Apelidos { get; set; } = string.Empty;   // separados por "\n"

    public List<LigacaoNoIndice> Ligacoes { get; set; } = [];
    public List<EtiquetaNoIndice> Etiquetas { get; set; } = [];
}

/// <summary>Uma ligação que SAI de uma nota. <see cref="Destino"/> nulo = ligação quebrada.</summary>
public sealed class LigacaoNoIndice
{
    public int Id { get; set; }
    public int NotaId { get; set; }
    public NotaNoIndice Nota { get; set; } = null!;
    public string Alvo { get; set; } = string.Empty;
    public string? Destino { get; set; }
    public string? Secao { get; set; }
    public string? Rotulo { get; set; }
    public int Forma { get; set; }
    public int Posicao { get; set; }
}

public sealed class EtiquetaNoIndice
{
    public int Id { get; set; }
    public int NotaId { get; set; }
    public NotaNoIndice Nota { get; set; } = null!;
    public string Valor { get; set; } = string.Empty;
}

/// <summary>Uma versão anterior de uma nota. NÃO é índice: é o único lugar onde este texto ainda existe.</summary>
public sealed class RevisaoNoIndice
{
    public long Id { get; set; }
    public string Caminho { get; set; } = string.Empty;
    public string Conteudo { get; set; } = string.Empty;
    public DateTimeOffset Em { get; set; }
    public string? Autor { get; set; }
}

/// <summary>
/// O banco do Learn.
///
/// ATENÇÃO A UMA DISTINÇÃO QUE ESTE CONTEXTO MISTURA DE PROPÓSITO, e que precisa ficar registrada: as
/// tabelas de ÍNDICE (notas, ligações, etiquetas) são descartáveis — apagar e reconstruir a partir do
/// vault é operação de rotina. A tabela de REVISÕES não é: ela guarda texto que não existe mais em
/// arquivo nenhum. Compartilham a mesma conexão por economia de infraestrutura, nunca por terem a mesma
/// natureza. Qualquer rotina de "reindexar do zero" tem de truncar as três primeiras e não encostar na
/// quarta — e é por isso que <see cref="Portas.IHistoricoDeNotas"/> é uma porta separada do índice.
/// </summary>
public sealed class ContextoDoIndice(DbContextOptions<ContextoDoIndice> opcoes) : DbContext(opcoes)
{
    public DbSet<NotaNoIndice> Notas => Set<NotaNoIndice>();
    public DbSet<LigacaoNoIndice> Ligacoes => Set<LigacaoNoIndice>();
    public DbSet<EtiquetaNoIndice> Etiquetas => Set<EtiquetaNoIndice>();
    public DbSet<RevisaoNoIndice> Revisoes => Set<RevisaoNoIndice>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<NotaNoIndice>(e =>
        {
            e.ToTable("notas");
            e.HasIndex(x => x.Caminho).IsUnique();
            e.Property(x => x.Caminho).HasMaxLength(1024);
            e.Property(x => x.Titulo).HasMaxLength(512);
            e.Property(x => x.Impressao).HasMaxLength(64);
            // A ordenação por data é a consulta mais frequente da interface (lista de recentes, que abre
            // com o app). Sem índice, ela vira varredura de tabela a cada carregamento de tela.
            e.HasIndex(x => x.ModificadoEm);
        });

        b.Entity<LigacaoNoIndice>(e =>
        {
            e.ToTable("ligacoes");
            e.HasOne(x => x.Nota).WithMany(n => n.Ligacoes).HasForeignKey(x => x.NotaId).OnDelete(DeleteBehavior.Cascade);
            // O índice por DESTINO é o que faz o painel de backlinks abrir instantaneamente: "quem aponta
            // para cá" é exatamente uma busca por esta coluna, e é a pergunta mais valiosa do produto.
            e.HasIndex(x => x.Destino);
            e.Property(x => x.Alvo).HasMaxLength(1024);
            e.Property(x => x.Destino).HasMaxLength(1024);
        });

        b.Entity<EtiquetaNoIndice>(e =>
        {
            e.ToTable("etiquetas");
            e.HasOne(x => x.Nota).WithMany(n => n.Etiquetas).HasForeignKey(x => x.NotaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Valor);
            e.Property(x => x.Valor).HasMaxLength(512);
        });

        b.Entity<RevisaoNoIndice>(e =>
        {
            e.ToTable("revisoes");
            e.HasIndex(x => new { x.Caminho, x.Em });
            e.Property(x => x.Caminho).HasMaxLength(1024);
        });
    }
}
