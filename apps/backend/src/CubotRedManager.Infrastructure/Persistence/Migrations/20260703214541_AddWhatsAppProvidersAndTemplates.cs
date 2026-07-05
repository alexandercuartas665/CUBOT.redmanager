using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppProvidersAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cloud_access_token_encrypted",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cloud_business_account_id",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cloud_phone_number_id",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cloud_webhook_verify_token_encrypted",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            // Las lineas existentes son Evolution (la primera version del modulo solo soportaba
            // ese proveedor); marcar el default como "Evolution" preserva su comportamiento.
            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "whats_app_lines",
                type: "text",
                nullable: false,
                defaultValue: "Evolution");

            migrationBuilder.AddColumn<string>(
                name: "y_cloud_api_key_encrypted",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "y_cloud_phone_number_id",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "y_cloud_waba_id",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "y_cloud_webhook_secret_encrypted",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "whats_app_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    header_type = table.Column<string>(type: "text", nullable: true),
                    header_text = table.Column<string>(type: "text", nullable: true),
                    body_text = table.Column<string>(type: "text", nullable: false),
                    footer_text = table.Column<string>(type: "text", nullable: true),
                    variables_json = table.Column<string>(type: "jsonb", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    whats_app_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    waba_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    provider_template_id = table.Column<string>(type: "text", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whats_app_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_lines_y_cloud_phone_number_id",
                table: "whats_app_lines",
                column: "y_cloud_phone_number_id",
                unique: true,
                filter: "y_cloud_phone_number_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_templates_tenant_id_name_language",
                table: "whats_app_templates",
                columns: new[] { "tenant_id", "name", "language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "whats_app_templates");

            migrationBuilder.DropIndex(
                name: "ix_whats_app_lines_y_cloud_phone_number_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "cloud_access_token_encrypted",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "cloud_business_account_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "cloud_phone_number_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "cloud_webhook_verify_token_encrypted",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "y_cloud_api_key_encrypted",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "y_cloud_phone_number_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "y_cloud_waba_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "y_cloud_webhook_secret_encrypted",
                table: "whats_app_lines");
        }
    }
}
