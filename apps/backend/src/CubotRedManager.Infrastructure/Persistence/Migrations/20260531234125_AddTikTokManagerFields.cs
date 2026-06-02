using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTikTokManagerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bio",
                table: "social_accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "followers_count",
                table: "social_accounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pipeline_stage",
                table: "inbox_messages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "related_video_external_id",
                table: "inbox_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "related_video_thumb_url",
                table: "inbox_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "related_video_title",
                table: "inbox_messages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_tenant_id_pipeline_stage",
                table: "inbox_messages",
                columns: new[] { "tenant_id", "pipeline_stage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inbox_messages_tenant_id_pipeline_stage",
                table: "inbox_messages");

            migrationBuilder.DropColumn(
                name: "bio",
                table: "social_accounts");

            migrationBuilder.DropColumn(
                name: "followers_count",
                table: "social_accounts");

            migrationBuilder.DropColumn(
                name: "pipeline_stage",
                table: "inbox_messages");

            migrationBuilder.DropColumn(
                name: "related_video_external_id",
                table: "inbox_messages");

            migrationBuilder.DropColumn(
                name: "related_video_thumb_url",
                table: "inbox_messages");

            migrationBuilder.DropColumn(
                name: "related_video_title",
                table: "inbox_messages");
        }
    }
}
