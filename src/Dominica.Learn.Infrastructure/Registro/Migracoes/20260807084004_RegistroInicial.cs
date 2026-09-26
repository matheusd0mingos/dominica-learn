using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Dominica.Learn.Infrastructure.Registro.Migracoes
{
    /// <inheritdoc />
    public partial class RegistroInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lotes_de_questoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Usuario = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Materia = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Acertos = table.Column<int>(type: "integer", nullable: false),
                    Segundos = table.Column<int>(type: "integer", nullable: false),
                    Fonte = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lotes_de_questoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sessoes_de_estudo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Usuario = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Materia = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Segundos = table.Column<int>(type: "integer", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessoes_de_estudo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lotes_de_questoes_Usuario_Em",
                table: "lotes_de_questoes",
                columns: new[] { "Usuario", "Em" });

            migrationBuilder.CreateIndex(
                name: "IX_sessoes_de_estudo_Usuario_Inicio",
                table: "sessoes_de_estudo",
                columns: new[] { "Usuario", "Inicio" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lotes_de_questoes");

            migrationBuilder.DropTable(
                name: "sessoes_de_estudo");
        }
    }
}
