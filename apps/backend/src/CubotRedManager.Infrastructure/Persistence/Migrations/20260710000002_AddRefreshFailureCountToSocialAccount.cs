using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotRedManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Contador de fallos consecutivos del refresh de token. Cuando llega a un umbral (3) el
    /// servicio marca la cuenta como Expired aunque el access_token en DB parezca vivo.
    /// Cierra el false positive de "Conectada" con refresh_token invalidado por TikTok pero
    /// access_token nominalmente vigente.
    /// </summary>
    public partial class AddRefreshFailureCountToSocialAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "refresh_failure_count",
                table: "social_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refresh_failure_count",
                table: "social_accounts");
        }
    }
}
