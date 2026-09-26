using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Infrastructure.Indice.Migracoes
{
    /// <summary>
    /// Separa a etiqueta que a pessoa ESCREVEU do ancestral que o índice criou.
    /// Ver EtiquetaNoIndice.Propria.
    /// </summary>
    public partial class EtiquetaPropria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Propria",
                table: "etiquetas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // As linhas que já existem nascem com "false", e deixá-las assim faria o painel mostrar zero
            // notas paradas em cada disciplina até alguém reindexar — um número errado, em silêncio.
            //
            // Dá para reconstruir a verdade quase toda: um ancestral só foi criado porque existe um filho,
            // então toda linha SEM filho é uma etiqueta escrita à mão.
            //
            // O QUE ESTE SQL NÃO RECUPERA, dito às claras: a nota marcada com "#direito" E "#direito/penal"
            // ao mesmo tempo — ali o "#direito" foi escrito de verdade e vai ficar como ancestral. É o
            // único caso, ele erra para menos (nunca infla um número), e se corrige sozinho na próxima
            // gravação da nota, que reindexa tudo.
            migrationBuilder.Sql("""
                UPDATE etiquetas AS pai
                   SET "Propria" = TRUE
                 WHERE NOT EXISTS (
                       SELECT 1 FROM etiquetas AS filho
                        WHERE filho."NotaId" = pai."NotaId"
                          AND filho."Valor" LIKE pai."Valor" || '/%');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Propria",
                table: "etiquetas");
        }
    }
}
