using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Infrastructure.Registro.Migracoes
{
    /// <inheritdoc />
    public partial class VaultPorPessoa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Vault",
                table: "sessoes_de_estudo",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Vault",
                table: "lotes_de_questoes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // O QUE JÁ ESTAVA GRAVADO VAI PARA O VAULT PADRÃO, e esta linha é a diferença entre a
            // migração ser segura e apagar o registro de estudo de todo mundo.
            //
            // A coluna nasce com "" — é o padrão que o EF gera —, e o filtro global compara vault com
            // vault. Vazio nunca casa com "estudo". Sem este UPDATE, TODA hora estudada e TODA questão
            // resolvida ficariam na tabela, íntegras, e invisíveis para sempre: o painel mostraria zero e
            // ninguém ligaria o zero à migração de ontem.
            //
            // "estudo" e não outra coisa porque é exatamente para lá que a migração de disco leva o que
            // já existia — ver MigracaoParaVaults e NomeDoVault.Padrao. Registro e arquivos precisam
            // acabar no mesmo lugar, senão as horas ficam num vault e as notas noutro.
            migrationBuilder.Sql("UPDATE sessoes_de_estudo SET \"Vault\" = 'estudo' WHERE \"Vault\" = '';");
            migrationBuilder.Sql("UPDATE lotes_de_questoes SET \"Vault\" = 'estudo' WHERE \"Vault\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Vault",
                table: "sessoes_de_estudo");

            migrationBuilder.DropColumn(
                name: "Vault",
                table: "lotes_de_questoes");
        }
    }
}
