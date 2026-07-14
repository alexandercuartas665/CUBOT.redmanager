using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Portado desde CUBOT.travels. Tabla que vincula un AiAgent a una WhatsAppLine para que
    /// el agente atienda automaticamente los mensajes entrantes. Una linea solo puede tener
    /// UN binding activo (IsConnected=true) a la vez -> unique filtered index. Los bindings
    /// inactivos se mantienen como historial de reasignaciones.
    /// </summary>
    public partial class AddAiAgentLineBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_agent_line_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    whats_app_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_connected = table.Column<bool>(type: "boolean", nullable: false),
                    auto_confirm = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_line_bindings", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agent_line_bindings_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_agent_line_bindings_whats_app_lines_whats_app_line_id",
                        column: x => x.whats_app_line_id,
                        principalTable: "whats_app_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_tenant_id_agent_id",
                table: "ai_agent_line_bindings",
                columns: new[] { "tenant_id", "agent_id" });

            // Unico binding ACTIVO por (tenant, linea). Filtrado por IsConnected=true — permite
            // multiples bindings historicos inactivos para la misma linea.
            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_tenant_id_whats_app_line_id_is_conn",
                table: "ai_agent_line_bindings",
                columns: new[] { "tenant_id", "whats_app_line_id", "is_connected" },
                unique: true,
                filter: "\"is_connected\" = true");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_whats_app_line_id",
                table: "ai_agent_line_bindings",
                column: "whats_app_line_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ai_agent_line_bindings");
        }
    }
}
