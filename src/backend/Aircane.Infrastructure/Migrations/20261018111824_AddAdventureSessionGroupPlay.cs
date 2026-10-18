using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdventureSessionGroupPlay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveParticipantId",
                table: "AdventureSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentRound",
                table: "AdventureSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsGroup",
                table: "AdventureSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TurnOrder",
                table: "AdventureSessions",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveParticipantId",
                table: "AdventureSessions");

            migrationBuilder.DropColumn(
                name: "CurrentRound",
                table: "AdventureSessions");

            migrationBuilder.DropColumn(
                name: "IsGroup",
                table: "AdventureSessions");

            migrationBuilder.DropColumn(
                name: "TurnOrder",
                table: "AdventureSessions");
        }
    }
}
