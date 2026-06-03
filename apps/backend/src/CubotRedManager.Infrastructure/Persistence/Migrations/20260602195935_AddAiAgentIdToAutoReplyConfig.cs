using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAgentIdToAutoReplyConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ai_agent_id",
                table: "auto_reply_configs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_auto_reply_configs_ai_agent_id",
                table: "auto_reply_configs",
                column: "ai_agent_id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_reply_configs_ai_agents_ai_agent_id",
                table: "auto_reply_configs",
                column: "ai_agent_id",
                principalTable: "ai_agents",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auto_reply_configs_ai_agents_ai_agent_id",
                table: "auto_reply_configs");

            migrationBuilder.DropIndex(
                name: "ix_auto_reply_configs_ai_agent_id",
                table: "auto_reply_configs");

            migrationBuilder.DropColumn(
                name: "ai_agent_id",
                table: "auto_reply_configs");
        }
    }
}
