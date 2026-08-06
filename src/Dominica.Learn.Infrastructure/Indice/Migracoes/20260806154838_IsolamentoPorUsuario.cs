using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dominica.Learn.Infrastructure.Indice.Migracoes
{
    /// <summary>
    /// Cada linha passa a pertencer a um usuário, e o vault de cada um passa a viver em
    /// <c>{Vault:Raiz}/{apelido}</c>. Antes disto havia um vault só, compartilhado por todo mundo que se
    /// cadastrasse.
    ///
    /// O QUE ACONTECE COM O QUE JÁ EXISTIA, dito em voz alta porque é uma quebra:
    ///
    /// As linhas antigas não têm dono, e não há como adivinhar quem é — por isso nada aqui inventa um.
    /// As tabelas de ÍNDICE (notas, ligações, etiquetas) são esvaziadas, o que é seguro e rotineiro:
    /// elas são derivadas do disco por definição e a primeira reconciliação as reconstrói.
    ///
    /// REVISÕES NÃO SÃO ESVAZIADAS. Aquele texto não existe em arquivo nenhum, então apagá-lo seria
    /// perda de verdade. As revisões antigas ficam sem dono e, portanto, inalcançáveis pela aplicação.
    /// Quem já tinha dados e quer adotá-los precisa dizer de quem são — uma linha, com o apelido certo:
    ///
    ///     UPDATE revisoes SET "Usuario" = 'matheus' WHERE "Usuario" = '';
    ///
    /// E, no disco, mover o conteúdo do vault antigo para dentro da pasta daquele apelido:
    ///
    ///     mkdir -p /dados/vault/matheus &amp;&amp; mv /dados/vault/*.md /dados/vault/matheus/
    /// </summary>
    public partial class IsolamentoPorUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_revisoes_Caminho_Em",
                table: "revisoes");

            migrationBuilder.DropIndex(
                name: "IX_notas_Caminho",
                table: "notas");

            migrationBuilder.DropIndex(
                name: "IX_ligacoes_Destino",
                table: "ligacoes");

            migrationBuilder.DropIndex(
                name: "IX_etiquetas_Valor",
                table: "etiquetas");

            migrationBuilder.AddColumn<string>(
                name: "Usuario",
                table: "revisoes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Usuario",
                table: "notas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Usuario",
                table: "ligacoes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Usuario",
                table: "etiquetas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

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

            // As linhas anteriores ficaram com usuário vazio e nunca mais casariam com o filtro global —
            // seriam lixo invisível ocupando espaço para sempre. O índice é derivado do disco: esvaziar é
            // operação de rotina, e a primeira reconciliação o reconstrói. As ligações e etiquetas caem
            // junto por cascata, mas a exclusão explícita deixa a intenção legível.
            //
            // "revisoes" NÃO entra aqui: aquele texto não existe em arquivo nenhum. Ver o resumo da classe.
            migrationBuilder.Sql("DELETE FROM etiquetas;");
            migrationBuilder.Sql("DELETE FROM ligacoes;");
            migrationBuilder.Sql("DELETE FROM notas;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropColumn(
                name: "Usuario",
                table: "revisoes");

            migrationBuilder.DropColumn(
                name: "Usuario",
                table: "notas");

            migrationBuilder.DropColumn(
                name: "Usuario",
                table: "ligacoes");

            migrationBuilder.DropColumn(
                name: "Usuario",
                table: "etiquetas");

            migrationBuilder.CreateIndex(
                name: "IX_revisoes_Caminho_Em",
                table: "revisoes",
                columns: new[] { "Caminho", "Em" });

            migrationBuilder.CreateIndex(
                name: "IX_notas_Caminho",
                table: "notas",
                column: "Caminho",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ligacoes_Destino",
                table: "ligacoes",
                column: "Destino");

            migrationBuilder.CreateIndex(
                name: "IX_etiquetas_Valor",
                table: "etiquetas",
                column: "Valor");
        }
    }
}
