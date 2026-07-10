using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Historico de intentos de refresh/exchange OAuth por cuenta social. Diagnostico
    /// de tokens que caen: sin este historial, tras 5 min de un refresh fallido no queda
    /// rastro (los logs de Railway rotan y solo el ultimo error queda en LastSyncError).
    /// Retencion recomendada: 7 dias (rotacion via job periodico).
    /// NO guarda tokens ni auth_codes (regla CLAUDE.md).
    /// </summary>
    public partial class AddTokenRefreshLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "token_refresh_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    operation = table.Column<string>(type: "text", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    flavor = table.Column<string>(type: "text", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    response_code = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    failure_count_after = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_refresh_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_token_refresh_logs_social_accounts_social_account_id",
                        column: x => x.social_account_id,
                        principalTable: "social_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_token_refresh_logs_social_account_id_attempted_at",
                table: "token_refresh_logs",
                columns: new[] { "social_account_id", "attempted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "token_refresh_logs");
        }
    }
}
