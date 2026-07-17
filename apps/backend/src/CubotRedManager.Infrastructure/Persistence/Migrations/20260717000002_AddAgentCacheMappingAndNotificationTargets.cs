using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Portado desde CUBOT.travels. Crea las tablas ai_agent_cache_lead_mappings y
    /// ai_agent_notification_targets, requeridas por Fase 3 (AgentDispatcher + LeadMarker stub +
    /// PedidoMarkerProcessor). Sin estas tablas el dispatcher no puede armar el pedido a gerencia.
    /// </summary>
    public partial class AddAgentCacheMappingAndNotificationTargets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_agent_cache_lead_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cache_field_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    target_selector = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_cache_lead_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agent_cache_lead_mappings_ai_agents_agent_id",
                        column: x => x.agent_id, principalTable: "ai_agents",
                        principalColumn: "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_cache_lead_mappings_agent_id_cache_field_key",
                table: "ai_agent_cache_lead_mappings",
                columns: new[] { "agent_id", "cache_field_key" }, unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_cache_lead_mappings_tenant_id_agent_id",
                table: "ai_agent_cache_lead_mappings",
                columns: new[] { "tenant_id", "agent_id" });

            migrationBuilder.CreateTable(
                name: "ai_agent_notification_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_whats_app_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_value = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_notification_targets", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agent_notification_targets_ai_agents_agent_id",
                        column: x => x.agent_id, principalTable: "ai_agents",
                        principalColumn: "id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_agent_notification_targets_whats_app_lines_from_whats_app_line_id",
                        column: x => x.from_whats_app_line_id, principalTable: "whats_app_lines",
                        principalColumn: "id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_notification_targets_agent_id",
                table: "ai_agent_notification_targets",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_notification_targets_from_whats_app_line_id",
                table: "ai_agent_notification_targets",
                column: "from_whats_app_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_notification_targets_tenant_id_agent_id_sort_order",
                table: "ai_agent_notification_targets",
                columns: new[] { "tenant_id", "agent_id", "sort_order" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ai_agent_notification_targets");
            migrationBuilder.DropTable(name: "ai_agent_cache_lead_mappings");
        }
    }
}
