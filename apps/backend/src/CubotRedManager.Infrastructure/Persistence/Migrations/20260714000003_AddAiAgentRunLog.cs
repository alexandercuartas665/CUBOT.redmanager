using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Portado desde CUBOT.travels. Bitacora de atencion del agente IA: cada evento del
    /// pipeline de despacho (Inbound/Prompt/Tool/Reply/Info/Error) se persiste con su
    /// contenido y respuesta asociada. Dos indices para las dos consultas naturales: por
    /// conversacion (hilo de un chat) y por agente (auditoria transversal).
    /// </summary>
    public partial class AddAiAgentRunLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_agent_run_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    response = table.Column<string>(type: "text", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_run_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_run_logs_tenant_id_agent_id_occurred_at",
                table: "ai_agent_run_logs",
                columns: new[] { "tenant_id", "agent_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_run_logs_tenant_id_conversation_id_occurred_at",
                table: "ai_agent_run_logs",
                columns: new[] { "tenant_id", "conversation_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ai_agent_run_logs");
        }
    }
}
