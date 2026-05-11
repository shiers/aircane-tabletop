using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSystemDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── GameSystemDefinitions table ───────────────────────────────────
            migrationBuilder.CreateTable(
                name: "GameSystemDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Identifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Publisher = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    License = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSystemDefinitions", x => x.Id);
                });

            // ── GameSystemDefinitionVersions table ────────────────────────────
            migrationBuilder.CreateTable(
                name: "GameSystemDefinitionVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameSystemDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSystemDefinitionVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSystemDefinitionVersions_GameSystemDefinitions_GameSystemDefinitionId",
                        column: x => x.GameSystemDefinitionId,
                        principalTable: "GameSystemDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── Add FK columns to Campaigns ───────────────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "GameSystemDefinitionId",
                table: "Campaigns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GameSystemDefinitionVersionId",
                table: "Campaigns",
                type: "uuid",
                nullable: true);

            // ── Add FK column to Characters ───────────────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "GameSystemDefinitionId",
                table: "Characters",
                type: "uuid",
                nullable: true);

            // ── Indexes ───────────────────────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_GameSystemDefinitions_Identifier",
                table: "GameSystemDefinitions",
                column: "Identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSystemDefinitionVersions_GameSystemDefinitionId",
                table: "GameSystemDefinitionVersions",
                column: "GameSystemDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_GameSystemDefinitionId",
                table: "Campaigns",
                column: "GameSystemDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_GameSystemDefinitionId",
                table: "Characters",
                column: "GameSystemDefinitionId");

            // ── Foreign keys on Campaigns ─────────────────────────────────────
            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_GameSystemDefinitions_GameSystemDefinitionId",
                table: "Campaigns",
                column: "GameSystemDefinitionId",
                principalTable: "GameSystemDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_GameSystemDefinitionVersions_GameSystemDefinitionVersionId",
                table: "Campaigns",
                column: "GameSystemDefinitionVersionId",
                principalTable: "GameSystemDefinitionVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Foreign key on Characters ─────────────────────────────────────
            migrationBuilder.AddForeignKey(
                name: "FK_Characters_GameSystemDefinitions_GameSystemDefinitionId",
                table: "Characters",
                column: "GameSystemDefinitionId",
                principalTable: "GameSystemDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── Remove foreign keys ───────────────────────────────────────────
            migrationBuilder.DropForeignKey(
                name: "FK_Characters_GameSystemDefinitions_GameSystemDefinitionId",
                table: "Characters");

            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_GameSystemDefinitionVersions_GameSystemDefinitionVersionId",
                table: "Campaigns");

            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_GameSystemDefinitions_GameSystemDefinitionId",
                table: "Campaigns");

            // ── Remove indexes ────────────────────────────────────────────────
            migrationBuilder.DropIndex(
                name: "IX_Characters_GameSystemDefinitionId",
                table: "Characters");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_GameSystemDefinitionId",
                table: "Campaigns");

            // ── Remove FK columns ─────────────────────────────────────────────
            migrationBuilder.DropColumn(
                name: "GameSystemDefinitionId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "GameSystemDefinitionVersionId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "GameSystemDefinitionId",
                table: "Campaigns");

            // ── Drop tables ───────────────────────────────────────────────────
            migrationBuilder.DropTable(
                name: "GameSystemDefinitionVersions");

            migrationBuilder.DropTable(
                name: "GameSystemDefinitions");
        }
    }
}
