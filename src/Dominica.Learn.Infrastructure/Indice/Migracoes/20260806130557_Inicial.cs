using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Dominica.Learn.Infrastructure.Indice.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Caminho = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Titulo = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Resumo = table.Column<string>(type: "text", nullable: false),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    Impressao = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModificadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Palavras = table.Column<int>(type: "integer", nullable: false),
                    Apelidos = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "revisoes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Caminho = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    Em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Autor = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revisoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "etiquetas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotaId = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_etiquetas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_etiquetas_notas_NotaId",
                        column: x => x.NotaId,
                        principalTable: "notas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ligacoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotaId = table.Column<int>(type: "integer", nullable: false),
                    Alvo = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Destino = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Secao = table.Column<string>(type: "text", nullable: true),
                    Rotulo = table.Column<string>(type: "text", nullable: true),
                    Forma = table.Column<int>(type: "integer", nullable: false),
                    Posicao = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ligacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ligacoes_notas_NotaId",
                        column: x => x.NotaId,
                        principalTable: "notas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_etiquetas_NotaId",
                table: "etiquetas",
                column: "NotaId");

            migrationBuilder.CreateIndex(
                name: "IX_etiquetas_Valor",
                table: "etiquetas",
                column: "Valor");

            migrationBuilder.CreateIndex(
                name: "IX_ligacoes_Destino",
                table: "ligacoes",
                column: "Destino");

            migrationBuilder.CreateIndex(
                name: "IX_ligacoes_NotaId",
                table: "ligacoes",
                column: "NotaId");

            migrationBuilder.CreateIndex(
                name: "IX_notas_Caminho",
                table: "notas",
                column: "Caminho",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notas_ModificadoEm",
                table: "notas",
                column: "ModificadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_revisoes_Caminho_Em",
                table: "revisoes",
                columns: new[] { "Caminho", "Em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "etiquetas");

            migrationBuilder.DropTable(
                name: "ligacoes");

            migrationBuilder.DropTable(
                name: "revisoes");

            migrationBuilder.DropTable(
                name: "notas");
        }
    }
}
