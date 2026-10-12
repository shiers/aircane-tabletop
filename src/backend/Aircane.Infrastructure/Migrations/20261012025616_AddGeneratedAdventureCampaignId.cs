using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneratedAdventureCampaignId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "GeneratedAdventures",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedAdventures_CampaignId",
                table: "GeneratedAdventures",
                column: "CampaignId");

            migrationBuilder.AddForeignKey(
                name: "FK_GeneratedAdventures_Campaigns_CampaignId",
                table: "GeneratedAdventures",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GeneratedAdventures_Campaigns_CampaignId",
                table: "GeneratedAdventures");

            migrationBuilder.DropIndex(
                name: "IX_GeneratedAdventures_CampaignId",
                table: "GeneratedAdventures");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "GeneratedAdventures");
        }
    }
}
