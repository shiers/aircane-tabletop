using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdventureSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdventureSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAdventureId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentSceneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CompletedSceneIds = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OutcomeNotes = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NpcStates = table.Column<string>(type: "jsonb", nullable: false),
                    RevealedClueIds = table.Column<string>(type: "jsonb", nullable: false),
                    RevealedSecretIds = table.Column<string>(type: "jsonb", nullable: false),
                    EncounterOutcomes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdventureSessions_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdventureSessions_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AdventureSessions_GeneratedAdventures_GeneratedAdventureId",
                        column: x => x.GeneratedAdventureId,
                        principalTable: "GeneratedAdventures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdventureSessions_CampaignId_Status",
                table: "AdventureSessions",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AdventureSessions_CharacterId",
                table: "AdventureSessions",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_AdventureSessions_GeneratedAdventureId",
                table: "AdventureSessions",
                column: "GeneratedAdventureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdventureSessions");
        }
    }
}
