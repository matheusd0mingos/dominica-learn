using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Web.Data.Migracoes
{
    /// <inheritdoc />
    public partial class AcompanhamentoDeEstudo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acompanhamento_de_estudo",
                columns: table => new
                {
                    Dono = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Vault = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Convidado = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acompanhamento_de_estudo", x => new { x.Dono, x.Vault, x.Convidado });
                });

            migrationBuilder.CreateIndex(
                name: "IX_acompanhamento_de_estudo_Convidado",
                table: "acompanhamento_de_estudo",
                column: "Convidado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acompanhamento_de_estudo");
        }
    }
}
