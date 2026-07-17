using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Guarda el binario del recurso del agente directamente en la BD (columna bytea).
    /// Motivo: el filesystem de Railway es efimero — archivos escritos a wwwroot/uploads
    /// se pierden entre restarts del pod. Guardar el binario en la BD hace que el recurso
    /// sobreviva a cualquier reinicio y viaje con el resto del estado del tenant.
    /// </summary>
    public partial class AddAgentResourceBinaryContent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "file_content",
                table: "ai_agent_resources",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "file_mime_type",
                table: "ai_agent_resources",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "file_mime_type", table: "ai_agent_resources");
            migrationBuilder.DropColumn(name: "file_content", table: "ai_agent_resources");
        }
    }
}
