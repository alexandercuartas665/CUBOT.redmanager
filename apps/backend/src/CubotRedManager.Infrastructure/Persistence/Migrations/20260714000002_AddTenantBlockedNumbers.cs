using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Portado desde CUBOT.travels. Lista negra global del tenant: numeros de telefono a los que
    /// ningun agente de IA responde. La comparacion se hace por digitos ya normalizados; el
    /// indice unico por (tenant, phone) evita duplicados.
    /// </summary>
    public partial class AddTenantBlockedNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_blocked_numbers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_blocked_numbers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_blocked_numbers_tenant_id_phone",
                table: "tenant_blocked_numbers",
                columns: new[] { "tenant_id", "phone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "tenant_blocked_numbers");
        }
    }
}
