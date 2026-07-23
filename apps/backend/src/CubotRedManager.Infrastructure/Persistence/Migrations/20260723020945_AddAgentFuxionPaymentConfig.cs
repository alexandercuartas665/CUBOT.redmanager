using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentFuxionPaymentConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_api_base_url",
                table: "ai_agents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_api_path_template",
                table: "ai_agents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_catalog_container_name",
                table: "ai_agents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_catalog_name_column",
                table: "ai_agents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_catalog_product_id_column",
                table: "ai_agents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_country",
                table: "ai_agents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "payment_enabled",
                table: "ai_agents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "payment_response_url_path",
                table: "ai_agents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_token_encrypted",
                table: "ai_agents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "payment_token_expires_at",
                table: "ai_agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "payment_token_last_verified_at",
                table: "ai_agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_user_id",
                table: "ai_agents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_api_base_url",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_api_path_template",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_catalog_container_name",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_catalog_name_column",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_catalog_product_id_column",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_country",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_enabled",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_response_url_path",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_token_encrypted",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_token_expires_at",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_token_last_verified_at",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "payment_user_id",
                table: "ai_agents");
        }
    }
}
