using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Web.Data.Migracoes
{
    /// <summary>
    /// O teto diário de cartões inéditos. Ver TetoDeCartoesNovos.
    /// </summary>
    public partial class CartoesNovosPorDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CartoesNovosPorDia",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                // VINTE, E NÃO ZERO. O gerador escreveu "defaultValue: 0" porque é o padrão de um int, e
                // zero neste domínio significa SEM TETO — o oposto exato da intenção. Toda conta que já
                // existia teria estreado sem limite nenhum, em silêncio, e o recurso pareceria não
                // funcionar. Foi visto no banco antes de ir para qualquer lugar; a linha existe para que
                // não volte.
                defaultValue: 20);

            // As contas que já existem também: o valor padrão da coluna só vale para linha nova.
            migrationBuilder.Sql(@"UPDATE ""AspNetUsers"" SET ""CartoesNovosPorDia"" = 20;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CartoesNovosPorDia",
                table: "AspNetUsers");
        }
    }
}
