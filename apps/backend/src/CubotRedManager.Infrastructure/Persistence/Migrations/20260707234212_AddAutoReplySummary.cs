using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoReplySummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "summary_enabled",
                table: "auto_reply_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "summary_line_id",
                table: "auto_reply_configs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary_target",
                table: "auto_reply_configs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary_target_type",
                table: "auto_reply_configs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "summary_template",
                table: "auto_reply_configs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_auto_reply_configs_summary_line_id",
                table: "auto_reply_configs",
                column: "summary_line_id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_reply_configs_whats_app_lines_summary_line_id",
                table: "auto_reply_configs",
                column: "summary_line_id",
                principalTable: "whats_app_lines",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auto_reply_configs_whats_app_lines_summary_line_id",
                table: "auto_reply_configs");

            migrationBuilder.DropIndex(
                name: "ix_auto_reply_configs_summary_line_id",
                table: "auto_reply_configs");

            migrationBuilder.DropColumn(
                name: "summary_enabled",
                table: "auto_reply_configs");

            migrationBuilder.DropColumn(
                name: "summary_line_id",
                table: "auto_reply_configs");

            migrationBuilder.DropColumn(
                name: "summary_target",
                table: "auto_reply_configs");

            migrationBuilder.DropColumn(
                name: "summary_target_type",
                table: "auto_reply_configs");

            migrationBuilder.DropColumn(
                name: "summary_template",
                table: "auto_reply_configs");
        }
    }
}
