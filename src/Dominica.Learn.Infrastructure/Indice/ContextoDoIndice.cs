using Microsoft.EntityFrameworkCore;

namespace Dominica.Learn.Infrastructure.Indice;

/// <summary>Uma nota no índice. Espelha o arquivo; nada aqui é dado original do usuário.</summary>
public sealed class NotaNoIndice
{
    /// <summary>
    /// De quem é esta linha. É a fronteira entre o conhecimento de uma pessoa e o de outra, e ela é
    /// aplicada por FILTRO GLOBAL do EF Core — ver <see cref="ContextoDoIndice"/>.
    /// </summary>
    public string Usuario { get; set; } = string.Empty;

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
    /// <summary>
    /// De quem é esta linha. É a fronteira entre o conhecimento de uma pessoa e o de outra, e ela é
    /// aplicada por FILTRO GLOBAL do EF Core — ver <see cref="ContextoDoIndice"/>.
    /// </summary>
    public string Usuario { get; set; } = string.Empty;

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
    /// <summary>
    /// De quem é esta linha. É a fronteira entre o conhecimento de uma pessoa e o de outra, e ela é
    /// aplicada por FILTRO GLOBAL do EF Core — ver <see cref="ContextoDoIndice"/>.
    /// </summary>
    public string Usuario { get; set; } = string.Empty;

    public int Id { get; set; }
    public int NotaId { get; set; }
    public NotaNoIndice Nota { get; set; } = null!;
    public string Valor { get; set; } = string.Empty;

    /// <summary>
    /// A pessoa ESCREVEU esta etiqueta na nota, ou ela está aqui só como ancestral de outra?
    ///
    /// A linha "direito" existe para toda nota marcada com "#direito/penal" — é o que faz "tudo de
    /// #direito" funcionar sem virar um LIKE que casaria "#direitos-humanos". Mas essa expansão apaga uma
    /// distinção real: quem está marcado SÓ como "#direito", sem tópico, é justamente o que ficou por
    /// organizar. Sem esta coluna, o painel mostraria "3 notas paradas em #direito" quando são zero — um
    /// número que não dá erro nenhum e vira a medida usada para decidir o que estudar.
    /// </summary>
    public bool Propria { get; set; }
}

/// <summary>Uma versão anterior de uma nota. NÃO é índice: é o único lugar onde este texto ainda existe.</summary>
public sealed class RevisaoNoIndice
{
    /// <summary>
    /// De quem é esta linha. É a fronteira entre o conhecimento de uma pessoa e o de outra, e ela é
    /// aplicada por FILTRO GLOBAL do EF Core — ver <see cref="ContextoDoIndice"/>.
    /// </summary>
    public string Usuario { get; set; } = string.Empty;

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
    /// <summary>
    /// De quem é o vault que este contexto enxerga. Todo filtro global desta classe compara com ele.
    ///
    /// O PADRÃO É VAZIO, E ISSO É A PROTEÇÃO: nenhuma linha real tem usuário vazio, então um adaptador
    /// que esquecesse de preencher este campo devolveria uma lista vazia — nunca a nota de outra pessoa.
    /// Falhar fechado é a única forma aceitável de falhar aqui, porque vazamento de vault não dá erro,
    /// não aparece em log, e só é descoberto pelo dono da nota.
    /// </summary>
    public string UsuarioAtual { get; set; } = string.Empty;

    public DbSet<NotaNoIndice> Notas => Set<NotaNoIndice>();
    public DbSet<LigacaoNoIndice> Ligacoes => Set<LigacaoNoIndice>();
    public DbSet<EtiquetaNoIndice> Etiquetas => Set<EtiquetaNoIndice>();
    public DbSet<RevisaoNoIndice> Revisoes => Set<RevisaoNoIndice>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<NotaNoIndice>(e =>
        {
            e.ToTable("notas");
            // O caminho é único DENTRO de um vault, não no banco. Sem o usuário na chave, a segunda
            // pessoa a criar "Direito/Licitações.md" esbarraria na nota da primeira.
            e.HasIndex(x => new { x.Usuario, x.Caminho }).IsUnique();
            e.Property(x => x.Usuario).HasMaxLength(32);
            e.HasQueryFilter(x => x.Usuario == UsuarioAtual);
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
            // Backlink é a consulta mais valiosa do produto e roda em toda abertura de nota: o índice
            // inclui o usuário porque a consulta sempre inclui — o filtro global põe as duas colunas
            // no WHERE, e um índice só por destino faria o banco varrer as ligações de todo mundo.
            e.HasIndex(x => new { x.Usuario, x.Destino });
            e.Property(x => x.Usuario).HasMaxLength(32);
            e.Property(x => x.Alvo).HasMaxLength(1024);
            e.Property(x => x.Destino).HasMaxLength(1024);
            e.HasQueryFilter(x => x.Usuario == UsuarioAtual);
        });

        b.Entity<EtiquetaNoIndice>(e =>
        {
            e.ToTable("etiquetas");
            e.HasOne(x => x.Nota).WithMany(n => n.Etiquetas).HasForeignKey(x => x.NotaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Usuario, x.Valor });
            e.Property(x => x.Usuario).HasMaxLength(32);
            e.Property(x => x.Valor).HasMaxLength(512);
            e.HasQueryFilter(x => x.Usuario == UsuarioAtual);
        });

        b.Entity<RevisaoNoIndice>(e =>
        {
            e.ToTable("revisoes");
            e.HasIndex(x => new { x.Usuario, x.Caminho, x.Em });
            e.Property(x => x.Usuario).HasMaxLength(32);
            e.Property(x => x.Caminho).HasMaxLength(1024);
            // Revisão é o único texto que não existe em arquivo nenhum. O filtro aqui também fecha a
            // porta de buscar a revisão de outra pessoa por id — que seria adivinhável, já que é serial.
            e.HasQueryFilter(x => x.Usuario == UsuarioAtual);
        });
    }
}
