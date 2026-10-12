using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdventureSessionPendingRoll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PendingRollDC",
                table: "AdventureSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingRollLabel",
                table: "AdventureSessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingRollDC",
                table: "AdventureSessions");

            migrationBuilder.DropColumn(
                name: "PendingRollLabel",
                table: "AdventureSessions");
        }
    }
}
