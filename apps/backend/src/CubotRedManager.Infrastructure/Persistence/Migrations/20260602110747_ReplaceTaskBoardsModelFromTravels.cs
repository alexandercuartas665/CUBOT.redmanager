using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceTaskBoardsModelFromTravels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_cards_task_columns_column_id",
                table: "task_cards");

            migrationBuilder.DropTable(
                name: "task_columns");

            migrationBuilder.DropIndex(
                name: "ix_task_cards_column_id_sort_order",
                table: "task_cards");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "task_cards");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "task_cards");

            migrationBuilder.DropColumn(
                name: "related_publication_id",
                table: "task_cards");

            migrationBuilder.DropColumn(
                name: "related_social_account_id",
                table: "task_cards");

            migrationBuilder.DropColumn(
                name: "campaign_name",
                table: "task_boards");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "task_boards");

            migrationBuilder.RenameColumn(
                name: "tags_json",
                table: "task_cards",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "description_md",
                table: "task_cards",
                newName: "color");

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "task_cards",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "due_at",
                table: "task_cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                table: "task_cards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "task_boards",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "task_boards",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "task_boards",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                table: "task_boards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "task_boards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "task_board_columns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_board_columns", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_board_columns_task_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "task_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_activities_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_assignments_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_card_assignments_tenant_users_tenant_user_id",
                        column: x => x.tenant_user_id,
                        principalTable: "tenant_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_attachments_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_checklist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_checklist_items_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_tags_task_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "task_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_card_tag_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_card_tag_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_card_tag_assignments_task_card_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "task_card_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_card_tag_assignments_task_cards_task_card_id",
                        column: x => x.task_card_id,
                        principalTable: "task_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_cards_board_id",
                table: "task_cards",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_cards_column_id",
                table: "task_cards",
                column: "column_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_cards_tenant_id_board_id_column_id_sort_order",
                table: "task_cards",
                columns: new[] { "tenant_id", "board_id", "column_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_cards_tenant_id_is_archived",
                table: "task_cards",
                columns: new[] { "tenant_id", "is_archived" });

            migrationBuilder.CreateIndex(
                name: "ix_task_boards_tenant_id_sort_order",
                table: "task_boards",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_board_columns_board_id",
                table: "task_board_columns",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_board_columns_tenant_id_board_id_sort_order",
                table: "task_board_columns",
                columns: new[] { "tenant_id", "board_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_card_activities_task_card_id_created_at",
                table: "task_card_activities",
                columns: new[] { "task_card_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_card_assignments_task_card_id_tenant_user_id",
                table: "task_card_assignments",
                columns: new[] { "task_card_id", "tenant_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_card_assignments_tenant_user_id",
                table: "task_card_assignments",
                column: "tenant_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_card_attachments_task_card_id_created_at",
                table: "task_card_attachments",
                columns: new[] { "task_card_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_card_checklist_items_task_card_id_sort_order",
                table: "task_card_checklist_items",
                columns: new[] { "task_card_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_card_tag_assignments_tag_id",
                table: "task_card_tag_assignments",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_card_tag_assignments_task_card_id_tag_id",
                table: "task_card_tag_assignments",
                columns: new[] { "task_card_id", "tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_card_tags_board_id_name",
                table: "task_card_tags",
                columns: new[] { "board_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_task_cards_task_board_columns_column_id",
                table: "task_cards",
                column: "column_id",
                principalTable: "task_board_columns",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_cards_task_boards_board_id",
                table: "task_cards",
                column: "board_id",
                principalTable: "task_boards",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_cards_task_board_columns_column_id",
                table: "task_cards");

            migrationBuilder.DropForeignKey(
                name: "fk_task_cards_task_boards_board_id",
                table: "task_cards");

            migrationBuilder.DropTable(
                name: "task_board_columns");

            migrationBuilder.DropTable(
                name: "task_card_activities");

            migrationBuilder.DropTable(
                name: "task_card_assignments");

            migrationBuilder.DropTable(
                name: "task_card_attachments");

            migrationBuilder.DropTable(
                name: "task_card_checklist_items");

            migrationBuilder.DropTable(
                name: "task_card_tag_assignments");

            migrationBuilder.DropTable(
                name: "task_card_tags");

            migrationBuilder.DropIndex(
                name: "ix_task_cards_board_id",
                table: "task_cards");

            migrationBuilder.DropIndex(
                name: "ix_task_cards_column_id",
                table: "task_cards");

            migrationBuilder.DropIndex(
                name: "ix_task_cards_tenant_id_board_id_column_id_sort_order",
                table: "task_cards");

            migrationBuilder.DropIndex(
                name: "ix_task_cards_tenant_id_is_archived",
                table: "task_cards");

            migrationBuilder.DropIndex(
                name: "ix_task_boards_tenant_id_sort_order",
                table: "task_boards");

            migrationBuilder.DropColumn(
                name: "due_at",
                table: "task_cards");

            migrationBuilder.DropColumn(
                name: "is_archived",
                table: "task_cards");

            migrationBuilder.DropColumn(
                name: "color",
                table: "task_boards");

            migrationBuilder.DropColumn(
                name: "is_archived",
                table: "task_boards");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "task_boards");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "task_cards",
                newName: "tags_json");

            migrationBuilder.RenameColumn(
                name: "color",
                table: "task_cards",
                newName: "description_md");

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "task_cards",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "task_cards",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "priority",
                table: "task_cards",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "related_publication_id",
                table: "task_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "related_social_account_id",
                table: "task_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "task_boards",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "task_boards",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "campaign_name",
                table: "task_boards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "client_id",
                table: "task_boards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_columns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    color_hex = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_columns", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_columns_task_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "task_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_cards_column_id_sort_order",
                table: "task_cards",
                columns: new[] { "column_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_columns_board_id",
                table: "task_columns",
                column: "board_id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_cards_task_columns_column_id",
                table: "task_cards",
                column: "column_id",
                principalTable: "task_columns",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
