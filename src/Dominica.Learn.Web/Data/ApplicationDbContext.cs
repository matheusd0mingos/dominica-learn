using Dominica.Learn.Infrastructure;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    /// <summary>Quem pode acompanhar o painel de estudos de quem. Ver <see cref="AcompanhamentoNoBanco"/>.</summary>
    public DbSet<AcompanhamentoNoBanco> Acompanhamentos => Set<AcompanhamentoNoBanco>();

    /// <summary>As turmas e as matrículas — organização de convites, não autoridade. Ver <see cref="TurmaNoBanco"/>.</summary>
    public DbSet<TurmaNoBanco> Turmas => Set<TurmaNoBanco>();
    public DbSet<MatriculaNoBanco> Matriculas => Set<MatriculaNoBanco>();

    /// <summary>Todo DateTimeOffset atravessa a borda em UTC — ver <see cref="InstanteParaUtc"/>.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        base.ConfigureConventions(b);
        b.Properties<DateTimeOffset>().HaveConversion<InstanteParaUtc>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AcompanhamentoNoBanco>(e =>
        {
            e.ToTable("acompanhamento_de_estudo");
            e.Property(x => x.Dono).HasMaxLength(64);
            e.Property(x => x.Vault).HasMaxLength(64);
            e.Property(x => x.Convidado).HasMaxLength(64);
            // O ÍNDICE É PELO CONVIDADO porque a consulta mais frequente é a dele: "quais painéis eu
            // posso abrir?" roda a cada visita à tela de acompanhamento. A chave primária
            // (Dono, Vault, Convidado) já serve as consultas do dono e a checagem de permissão.
            e.HasIndex(x => x.Convidado);
        });

        b.Entity<TurmaNoBanco>(e =>
        {
            e.ToTable("turma");
            e.Property(x => x.Codigo).HasMaxLength(16);
            e.Property(x => x.Dono).HasMaxLength(64);
            e.Property(x => x.Nome).HasMaxLength(Dominica.Learn.Domain.Compartilhamento.Turma.TamanhoMaximoDoNome);
            e.HasIndex(x => x.Dono);   // "minhas turmas" é a consulta do professor a cada visita
        });

        b.Entity<MatriculaNoBanco>(e =>
        {
            e.ToTable("matricula_na_turma");
            e.Property(x => x.Turma).HasMaxLength(16);
            e.Property(x => x.Aluno).HasMaxLength(64);
            e.Property(x => x.Vault).HasMaxLength(64);
            e.HasIndex(x => x.Aluno);   // "em que turmas eu estou" é a consulta do aluno
        });
    }
}
