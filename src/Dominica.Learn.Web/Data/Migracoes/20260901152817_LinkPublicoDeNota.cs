using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Web.Data.Migracoes
{
    /// <inheritdoc />
    public partial class LinkPublicoDeNota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "link_de_nota",
                columns: table => new
                {
                    Token = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    Dono = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Vault = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Caminho = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_link_de_nota", x => x.Token);
                });

            migrationBuilder.CreateIndex(
                name: "IX_link_de_nota_Dono",
                table: "link_de_nota",
                column: "Dono");

            migrationBuilder.CreateIndex(
                name: "IX_link_de_nota_Dono_Vault_Caminho",
                table: "link_de_nota",
                columns: new[] { "Dono", "Vault", "Caminho" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "link_de_nota");
        }
    }
}
