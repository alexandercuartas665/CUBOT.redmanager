using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicationsAndInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    network_code = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    author_external_id = table.Column<string>(type: "text", nullable: true),
                    author_name = table.Column<string>(type: "text", nullable: true),
                    author_avatar_url = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    media_urls_json = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    assigned_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_publication_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caption = table.Column<string>(type: "text", nullable: false),
                    media_urls_json = table.Column<string>(type: "text", nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    author_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_refs_json = table.Column<string>(type: "text", nullable: true),
                    results_json = table.Column<string>(type: "text", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_replies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    sent_by_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    external_ref_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_replies", x => x.id);
                    table.ForeignKey(
                        name: "fk_inbox_replies_inbox_messages_inbox_message_id",
                        column: x => x.inbox_message_id,
                        principalTable: "inbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publication_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publication_targets", x => x.id);
                    table.ForeignKey(
                        name: "fk_publication_targets_publications_publication_id",
                        column: x => x.publication_id,
                        principalTable: "publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_network_code_external_id",
                table: "inbox_messages",
                columns: new[] { "network_code", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_replies_inbox_message_id",
                table: "inbox_replies",
                column: "inbox_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_publication_targets_publication_id",
                table: "publication_targets",
                column: "publication_id");

            migrationBuilder.CreateIndex(
                name: "ix_publications_tenant_id_scheduled_at",
                table: "publications",
                columns: new[] { "tenant_id", "scheduled_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_replies");

            migrationBuilder.DropTable(
                name: "publication_targets");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "publications");
        }
    }
}
