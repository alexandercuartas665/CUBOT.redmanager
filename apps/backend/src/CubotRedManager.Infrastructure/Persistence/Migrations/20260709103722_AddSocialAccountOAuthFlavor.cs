using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialAccountOAuthFlavor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_refresh_failure_notified_at",
                table: "social_accounts",
                type: "timestamp with time zone",
                nullable: true);

            // Cuentas historicas fueron canjeadas por el flujo BusinessV13 (business-api.tiktok.com).
            // Este default preserva su comportamiento hasta que se reconecten manualmente.
            migrationBuilder.AddColumn<string>(
                name: "o_auth_flavor",
                table: "social_accounts",
                type: "text",
                nullable: false,
                defaultValue: "BusinessV13");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_refresh_failure_notified_at",
                table: "social_accounts");

            migrationBuilder.DropColumn(
                name: "o_auth_flavor",
                table: "social_accounts");
        }
    }
}
