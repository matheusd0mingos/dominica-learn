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

    /// <summary>As notas publicadas em link. Ver <see cref="LinkDeNotaNoBanco"/>.</summary>
    public DbSet<LinkDeNotaNoBanco> LinksDeNota => Set<LinkDeNotaNoBanco>();

    /// <summary>Todo DateTimeOffset atravessa a borda em UTC — ver <see cref="InstanteParaUtc"/>.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        base.ConfigureConventions(b);
        b.Properties<DateTimeOffset>().HaveConversion<InstanteParaUtc>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // O APELIDO É ÚNICO, E QUEM GARANTE ISSO É O BANCO.
        //
        // Era garantido só por um "consulta e depois insere" no cadastro (Register.razor): pergunta se o
        // apelido existe, e se não existir, cria. Entre a pergunta e a criação cabe outro cadastro — e
        // aqui a consequência de perder essa corrida é a pior possível no produto inteiro: o apelido é o
        // NOME DA PASTA do vault em disco e o valor da coluna `Usuario` que filtra o registro. Dois
        // usuários com o mesmo apelido não ficam "parecidos": eles compartilham as mesmas notas e os
        // mesmos estudos, cada um vendo os do outro. Nenhuma verificação em C# fecha isso; índice fecha.
        //
        // FILTRO `<> ''` porque o apelido vazio existe como estado possível (a tela de Administração já
        // o trata: "(sem apelido)"), e uma instalação que tenha dois deles não conseguiria nem aplicar a
        // migração. O vazio é fechado na ORIGEM — ver a guarda no ExternalLogin —, não aqui.
        b.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.Apelido).HasMaxLength(Dominica.Learn.Domain.Vault.ApelidoDoUsuario.TamanhoMaximo);
            e.HasIndex(u => u.Apelido).IsUnique().HasFilter("\"Apelido\" <> ''");
        });
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

        b.Entity<LinkDeNotaNoBanco>(e =>
        {
            e.ToTable("link_de_nota");
            e.Property(x => x.Token).HasMaxLength(Dominica.Learn.Domain.Compartilhamento.TokenDeLink.Tamanho);
            e.Property(x => x.Dono).HasMaxLength(64);
            e.Property(x => x.Vault).HasMaxLength(64);
            e.Property(x => x.Caminho).HasMaxLength(1024);

            // ÚNICO POR (dono, vault, caminho), e a garantia é do BANCO e não só do adaptador. O
            // adaptador confere antes de inserir, mas duas abas publicando a mesma nota no mesmo instante
            // passam as duas pela conferência — e o resultado seriam dois tokens vivos para uma nota só.
            // Aí "revogar" mataria um, deixaria o outro no ar, e quem clicou sairia certo de ter fechado.
            e.HasIndex(x => new { x.Dono, x.Vault, x.Caminho }).IsUnique();

            // "o que eu publiquei" é a consulta da tela de gestão; sem ela não há como revogar o que se
            // esqueceu de ter aberto.
            e.HasIndex(x => x.Dono);
        });
    }
}
