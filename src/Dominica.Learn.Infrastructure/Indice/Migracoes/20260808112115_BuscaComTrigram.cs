using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Infrastructure.Indice.Migracoes
{
    /// <summary>
    /// ÍNDICES DE TRIGRAMA para a busca.
    ///
    /// A busca é ILIKE '%…%' de propósito (ver IndiceEmPostgres) — mas sem índice ela varre a tabela
    /// inteira, e "varre tudo" cresce junto com o vault. O pg_trgm indexa exatamente esse padrão: o
    /// ILIKE continua o mesmo, o plano é que muda. A porta não muda, a consulta não muda — só deixa de
    /// doer.
    ///
    /// DENTRO DE UM "DO" QUE ENGOLE FALTA DE PRIVILÉGIO, e isso é decisão: CREATE EXTENSION exige
    /// privilégio que o usuário "learn" pode não ter no VPS. Sem o bloco, a migração inteira falharia e
    /// derrubaria o app na subida — por causa de um índice de DESEMPENHO. Com ele, o app sobe, a busca
    /// funciona como sempre funcionou, e o aviso fica no log do Postgres. Quem quiser o índice roda
    /// "CREATE EXTENSION pg_trgm" como superusuário uma vez e re-executa a migração (ou cria os índices
    /// à mão — os comandos são estes daqui).
    /// </summary>
    public partial class BuscaComTrigram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    CREATE EXTENSION IF NOT EXISTS pg_trgm;
                    CREATE INDEX IF NOT EXISTS ix_notas_conteudo_trgm ON notas USING gin ("Conteudo" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS ix_notas_titulo_trgm   ON notas USING gin ("Titulo"   gin_trgm_ops);
                EXCEPTION WHEN insufficient_privilege THEN
                    RAISE NOTICE 'pg_trgm indisponível (sem privilégio): a busca segue sem índice de trigrama.';
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_notas_conteudo_trgm;
                DROP INDEX IF EXISTS ix_notas_titulo_trgm;
                """);
        }
    }
}
