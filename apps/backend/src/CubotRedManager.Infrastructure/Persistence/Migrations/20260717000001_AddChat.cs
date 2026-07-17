using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Portado desde CUBOT.travels. Crea las tablas conversations + messages para recibir y
    /// almacenar los mensajes WhatsApp entrantes / salientes. Sin esto el webhook receiver de
    /// Evolution no tiene donde persistir el mensaje.
    /// </summary>
    public partial class AddChat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    whats_app_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    agent_context_reset_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_contact_phone",
                table: "conversations",
                columns: new[] { "tenant_id", "contact_phone" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    message_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_by_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    media_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    media_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    media_mime_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    reaction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id",
                table: "messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_conversation_id",
                table: "messages",
                columns: new[] { "tenant_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_external_id",
                table: "messages",
                columns: new[] { "tenant_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "messages");
            migrationBuilder.DropTable(name: "conversations");
        }
    }
}
