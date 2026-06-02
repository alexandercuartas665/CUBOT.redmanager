using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoReply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auto_reply_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    max_replies_per_run = table.Column<int>(type: "integer", nullable: false),
                    delay_min_seconds = table.Column<int>(type: "integer", nullable: false),
                    delay_max_seconds = table.Column<int>(type: "integer", nullable: false),
                    blacklist_keywords = table.Column<string>(type: "text", nullable: true),
                    frequency = table.Column<string>(type: "text", nullable: false),
                    cron_custom = table.Column<string>(type: "text", nullable: true),
                    active_hours_mask = table.Column<int>(type: "integer", nullable: false),
                    active_days_of_week_mask = table.Column<byte>(type: "smallint", nullable: false),
                    default_template = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_reply_configs", x => x.id);
                    table.ForeignKey(
                        name: "fk_auto_reply_configs_social_accounts_social_account_id",
                        column: x => x.social_account_id,
                        principalTable: "social_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auto_reply_job_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    processed = table.Column<int>(type: "integer", nullable: false),
                    replied = table.Column<int>(type: "integer", nullable: false),
                    errors = table.Column<int>(type: "integer", nullable: false),
                    omitted = table.Column<int>(type: "integer", nullable: false),
                    trace = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_reply_job_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_auto_reply_job_logs_social_accounts_social_account_id",
                        column: x => x.social_account_id,
                        principalTable: "social_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auto_reply_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    keywords = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_reply_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_auto_reply_templates_auto_reply_configs_config_id",
                        column: x => x.config_id,
                        principalTable: "auto_reply_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auto_reply_configs_social_account_id",
                table: "auto_reply_configs",
                column: "social_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auto_reply_job_logs_social_account_id_started_at",
                table: "auto_reply_job_logs",
                columns: new[] { "social_account_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_auto_reply_templates_config_id_sort_order",
                table: "auto_reply_templates",
                columns: new[] { "config_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auto_reply_job_logs");

            migrationBuilder.DropTable(
                name: "auto_reply_templates");

            migrationBuilder.DropTable(
                name: "auto_reply_configs");
        }
    }
}
