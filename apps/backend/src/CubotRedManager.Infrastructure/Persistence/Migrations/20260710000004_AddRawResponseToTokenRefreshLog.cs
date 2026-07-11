using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Guarda el body crudo (sanitizado) de la respuesta de TikTok para diagnostico avanzado.
    /// Los tokens y auth_codes NO se guardan (SanitizeResponse los remueve). Truncado a 2KB.
    /// </summary>
    public partial class AddRawResponseToTokenRefreshLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "raw_response",
                table: "token_refresh_logs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "raw_response",
                table: "token_refresh_logs");
        }
    }
}
