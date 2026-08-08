using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Infrastructure.Registro;

/// <summary>Um lote de questões, como ele mora no banco.</summary>
public sealed class LoteNoRegistro
{
    public int Id { get; set; }
    public string Usuario { get; set; } = string.Empty;

    /// <summary>Em qual vault dessa pessoa o lançamento foi feito. Ver ContextoDoRegistro.VaultAtual.</summary>
    public string Vault { get; set; } = string.Empty;
    public DateTimeOffset Em { get; set; }
    public string Materia { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Acertos { get; set; }
    public int Segundos { get; set; }
    public string Fonte { get; set; } = string.Empty;
}

/// <summary>Uma resposta de cartão, como ela mora no banco. Ver RevisaoDeCartao no domínio.</summary>
public sealed class RevisaoNoRegistro
{
    public long Id { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Vault { get; set; } = string.Empty;
    public DateTimeOffset Em { get; set; }
    public string Materia { get; set; } = string.Empty;

    /// <summary>O enum Resposta como número: 0=Errei … 3=Fácil. Número porque a série é para SOMAR.</summary>
    public int Resposta { get; set; }
}

/// <summary>Uma sessão de estudo, como ela mora no banco.</summary>
public sealed class SessaoNoRegistro
{
    public int Id { get; set; }
    public string Usuario { get; set; } = string.Empty;

    /// <summary>Em qual vault dessa pessoa o lançamento foi feito. Ver ContextoDoRegistro.VaultAtual.</summary>
    public string Vault { get; set; } = string.Empty;
    public DateTimeOffset Inicio { get; set; }
    public string Materia { get; set; } = string.Empty;
    public int Segundos { get; set; }
    public string Observacao { get; set; } = string.Empty;
}

/// <summary>
/// O banco do REGISTRO — durável, e é a única coisa que precisa ser dita sobre ele.
///
/// BANCO PRÓPRIO, e não uma tabela a mais no índice. A regra do índice é "apagar e reconstruir é rotina,
/// não desastre", e ela está escrita no README e no ARQUITETURA.md. Uma tabela de registro ali dentro
/// seria apagada por alguém seguindo a documentação, e o dado não voltaria — não há de onde reconstruí-lo.
/// Separar os bancos põe a regra no NOME, onde ninguém precisa lembrar dela.
///
/// O filtro global por usuário é o mesmo mecanismo do índice: a fronteira entre pessoas é declarada uma
/// vez, e esquecê-la devolve vazio em vez de devolver o dado de outra pessoa.
/// </summary>
public sealed class ContextoDoRegistro(DbContextOptions<ContextoDoRegistro> opcoes) : DbContext(opcoes)
{
    /// <summary>De quem é a consulta. Ver o comentário homônimo em ContextoDoIndice.</summary>
    public string UsuarioAtual { get; set; } = string.Empty;

    /// <summary>
    /// E EM QUAL VAULT DELE. Horas e questões pertencem ao vault em que foram lançadas: o painel do
    /// trabalho não pode somar as horas de estudo, senão o número que decide a rotina mistura duas
    /// vidas. O registro que já existia foi para o vault padrão — ver a migração.
    /// </summary>
    public string VaultAtual { get; set; } = string.Empty;

    public DbSet<LoteNoRegistro> Lotes => Set<LoteNoRegistro>();
    public DbSet<SessaoNoRegistro> Sessoes => Set<SessaoNoRegistro>();
    public DbSet<RevisaoNoRegistro> Revisoes => Set<RevisaoNoRegistro>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LoteNoRegistro>(e =>
        {
            e.ToTable("lotes_de_questoes");
            e.Property(x => x.Usuario).HasMaxLength(32);
            e.Property(x => x.Vault).HasMaxLength(32);
            e.Property(x => x.Materia).HasMaxLength(256);
            e.Property(x => x.Fonte).HasMaxLength(256);
            // Toda consulta é "meu desempenho no período": o índice cobre exatamente isso.
            e.HasIndex(x => new { x.Usuario, x.Em });
            e.HasQueryFilter(x => x.Usuario == UsuarioAtual && x.Vault == VaultAtual);
        });

        b.Entity<SessaoNoRegistro>(e =>
        {
            e.ToTable("sessoes_de_estudo");
            e.Property(x => x.Usuario).HasMaxLength(32);
            e.Property(x => x.Vault).HasMaxLength(32);
            e.Property(x => x.Materia).HasMaxLength(256);
            e.Property(x => x.Observacao).HasMaxLength(512);
            e.HasIndex(x => new { x.Usuario, x.Inicio });
            e.HasQueryFilter(x => x.Usuario == UsuarioAtual && x.Vault == VaultAtual);
        });

        b.Entity<RevisaoNoRegistro>(e =>
        {
            e.ToTable("revisoes_de_cartao");
            e.Property(x => x.Usuario).HasMaxLength(32);
            e.Property(x => x.Vault).HasMaxLength(32);
            e.Property(x => x.Materia).HasMaxLength(256);
            e.HasIndex(x => new { x.Usuario, x.Em });
            e.HasQueryFilter(x => x.Usuario == UsuarioAtual && x.Vault == VaultAtual);
        });
    }
}
