using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// TikTokAppConfig deja de ser tenant-scoped: pasa a ser singleton de plataforma
    /// (una sola app TikTok registrada por CUBOT.redmanager sirve el OAuth de todas
    /// las agencias). Antes habia una fila por tenant (con indice unico por tenant_id).
    /// Al soltar la columna, se conservan las filas existentes; la logica del servicio
    /// hace First-or-create y asegura que a partir de aqui haya UNA sola fila.
    ///
    /// Si al momento del deploy existiera mas de una fila (caso raro: multi-tenant real),
    /// habria que consolidarlas manualmente ANTES de correr esta migracion. En el piloto
    /// actual solo existe la config del tenant demo.
    /// </summary>
    public partial class DropTenantIdFromTikTokAppConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tik_tok_app_configs_tenant_id",
                table: "tik_tok_app_configs");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "tik_tok_app_configs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "tik_tok_app_configs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_tik_tok_app_configs_tenant_id",
                table: "tik_tok_app_configs",
                column: "tenant_id",
                unique: true);
        }
    }
}
