using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionApprovalAndDenial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sessions that exist now were created before the setting existed, when every joiner needed
            // the host's approval. Keep it that way: a default of false here would silently turn them all
            // into "anyone with the code is approved".
            migrationBuilder.AddColumn<bool>(
                name: "RequireHostApproval",
                table: "Sessions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDenied",
                table: "SessionParticipants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireHostApproval",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "IsDenied",
                table: "SessionParticipants");
        }
    }
}
