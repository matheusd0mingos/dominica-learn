using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Web.Data.Migracoes
{
    /// <inheritdoc />
    public partial class ApelidoDoUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Apelido",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Apelido",
                table: "AspNetUsers");
        }
    }
}
