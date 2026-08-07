using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Infrastructure.Indice.Migracoes
{
    /// <inheritdoc />
    public partial class VaultPorPessoa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_revisoes_Usuario_Caminho_Em",
                table: "revisoes");

            migrationBuilder.DropIndex(
                name: "IX_notas_Usuario_Caminho",
                table: "notas");

            migrationBuilder.DropIndex(
                name: "IX_ligacoes_Usuario_Destino",
                table: "ligacoes");

            migrationBuilder.DropIndex(
                name: "IX_etiquetas_Usuario_Valor",
                table: "etiquetas");

            migrationBuilder.AddColumn<string>(
                name: "Vault",
                table: "revisoes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Vault",
                table: "notas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Vault",
                table: "ligacoes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Vault",
                table: "etiquetas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // O ÍNDICE É DESCARTÁVEL e o vigia o reconstrói na subida — mas as linhas antigas ficariam
            // com vault "" e nunca mais seriam lidas nem apagadas por ninguém: lixo permanente numa
            // tabela que se consulta o tempo todo. Carimbá-las com o padrão faz o reconciliador
            // encontrá-las onde espera, e o índice continua batendo com o disco desde o primeiro minuto.
            migrationBuilder.Sql("UPDATE notas SET \"Vault\" = 'estudo' WHERE \"Vault\" = '';");
            migrationBuilder.Sql("UPDATE ligacoes SET \"Vault\" = 'estudo' WHERE \"Vault\" = '';");
            migrationBuilder.Sql("UPDATE etiquetas SET \"Vault\" = 'estudo' WHERE \"Vault\" = '';");
            migrationBuilder.Sql("UPDATE revisoes SET \"Vault\" = 'estudo' WHERE \"Vault\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_revisoes_Usuario_Vault_Caminho_Em",
                table: "revisoes",
                columns: new[] { "Usuario", "Vault", "Caminho", "Em" });

            migrationBuilder.CreateIndex(
                name: "IX_notas_Usuario_Vault_Caminho",
                table: "notas",
                columns: new[] { "Usuario", "Vault", "Caminho" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ligacoes_Usuario_Vault_Destino",
                table: "ligacoes",
                columns: new[] { "Usuario", "Vault", "Destino" });

            migrationBuilder.CreateIndex(
                name: "IX_etiquetas_Usuario_Vault_Valor",
                table: "etiquetas",
                columns: new[] { "Usuario", "Vault", "Valor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_revisoes_Usuario_Vault_Caminho_Em",
                table: "revisoes");

            migrationBuilder.DropIndex(
                name: "IX_notas_Usuario_Vault_Caminho",
                table: "notas");

            migrationBuilder.DropIndex(
                name: "IX_ligacoes_Usuario_Vault_Destino",
                table: "ligacoes");

            migrationBuilder.DropIndex(
                name: "IX_etiquetas_Usuario_Vault_Valor",
                table: "etiquetas");

            migrationBuilder.DropColumn(
                name: "Vault",
                table: "revisoes");

            migrationBuilder.DropColumn(
                name: "Vault",
                table: "notas");

            migrationBuilder.DropColumn(
                name: "Vault",
                table: "ligacoes");

            migrationBuilder.DropColumn(
                name: "Vault",
                table: "etiquetas");

            migrationBuilder.CreateIndex(
                name: "IX_revisoes_Usuario_Caminho_Em",
                table: "revisoes",
                columns: new[] { "Usuario", "Caminho", "Em" });

            migrationBuilder.CreateIndex(
                name: "IX_notas_Usuario_Caminho",
                table: "notas",
                columns: new[] { "Usuario", "Caminho" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ligacoes_Usuario_Destino",
                table: "ligacoes",
                columns: new[] { "Usuario", "Destino" });

            migrationBuilder.CreateIndex(
                name: "IX_etiquetas_Usuario_Valor",
                table: "etiquetas",
                columns: new[] { "Usuario", "Valor" });
        }
    }
}
