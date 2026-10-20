using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantRejoinAndReplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejoinSecretHash",
                table: "SessionParticipants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByParticipantId",
                table: "SessionParticipants",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejoinSecretHash",
                table: "SessionParticipants");

            migrationBuilder.DropColumn(
                name: "ReplacedByParticipantId",
                table: "SessionParticipants");
        }
    }
}
