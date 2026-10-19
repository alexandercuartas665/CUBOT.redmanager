using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeConversationUniqueKeyAddWhatsAppLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversations_tenant_id_contact_phone",
                table: "conversations");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_contact_phone_whats_app_line_id",
                table: "conversations",
                columns: new[] { "tenant_id", "contact_phone", "whats_app_line_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversations_tenant_id_contact_phone_whats_app_line_id",
                table: "conversations");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_contact_phone",
                table: "conversations",
                columns: new[] { "tenant_id", "contact_phone" },
                unique: true);
        }
    }
}
