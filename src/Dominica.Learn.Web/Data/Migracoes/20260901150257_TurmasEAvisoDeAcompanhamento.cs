using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Web.Data.Migracoes
{
    /// <inheritdoc />
    public partial class TurmasEAvisoDeAcompanhamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VistoEm",
                table: "acompanhamento_de_estudo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "matricula_na_turma",
                columns: table => new
                {
                    Turma = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Aluno = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Vault = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntrouEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matricula_na_turma", x => new { x.Turma, x.Aluno });
                });

            migrationBuilder.CreateTable(
                name: "turma",
                columns: table => new
                {
                    Codigo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Dono = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turma", x => x.Codigo);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matricula_na_turma_Aluno",
                table: "matricula_na_turma",
                column: "Aluno");

            migrationBuilder.CreateIndex(
                name: "IX_turma_Dono",
                table: "turma",
                column: "Dono");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matricula_na_turma");

            migrationBuilder.DropTable(
                name: "turma");

            migrationBuilder.DropColumn(
                name: "VistoEm",
                table: "acompanhamento_de_estudo");
        }
    }
}
