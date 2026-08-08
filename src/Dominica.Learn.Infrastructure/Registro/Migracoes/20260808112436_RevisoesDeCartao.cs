using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Dominica.Learn.Infrastructure.Registro.Migracoes
{
    /// <inheritdoc />
    public partial class RevisoesDeCartao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "revisoes_de_cartao",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Usuario = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Vault = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Materia = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Resposta = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revisoes_de_cartao", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_revisoes_de_cartao_Usuario_Em",
                table: "revisoes_de_cartao",
                columns: new[] { "Usuario", "Em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revisoes_de_cartao");
        }
    }
}
