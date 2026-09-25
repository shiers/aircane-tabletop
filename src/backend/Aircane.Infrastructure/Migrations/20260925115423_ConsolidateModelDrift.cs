using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateModelDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: Tags (SourceDocuments) and Summary (Sessions) were present in the model
            // snapshot but never created by any recognized migration (the original
            // AddSourceDocumentTags / AddSessionSummary migrations lacked Designer files and
            // were therefore invisible to MigrateAsync). They are (re)created here so a fresh
            // database gets them. Guarded with IF NOT EXISTS so this migration is safe to apply
            // on databases that somehow already have the columns.
            migrationBuilder.Sql(
                "ALTER TABLE \"SourceDocuments\" ADD COLUMN IF NOT EXISTS \"Tags\" jsonb NOT NULL DEFAULT '[]';");
            migrationBuilder.Sql(
                "ALTER TABLE \"Sessions\" ADD COLUMN IF NOT EXISTS \"Summary\" text;");

            migrationBuilder.AddColumn<string>(
                name: "AttributionText",
                table: "SourceDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttributionUrl",
                table: "SourceDocuments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltIn",
                table: "SourceDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Marks a built-in rules text document (SRD, ORC, OGL content). Distinct from GameSystemDefinitions.IsBuiltIn which marks a built-in mechanic definition.");

            migrationBuilder.AddColumn<bool>(
                name: "IsDisabled",
                table: "SourceDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LicenseDisplayName",
                table: "SourceDocuments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseKey",
                table: "SourceDocuments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Formula",
                table: "Rolls",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<Guid>(
                name: "GameSystemDefinitionId",
                table: "Characters",
                type: "uuid",
                nullable: true);

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

            migrationBuilder.CreateTable(
                name: "AiActionProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiActionProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameStateSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentSceneId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StateJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameStateSnapshots", x => x.Id);
                });

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
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", maxLength: 2147483647, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSystemDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedAdventures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    PartyAnalysisJson = table.Column<string>(type: "text", nullable: true),
                    PitchJson = table.Column<string>(type: "text", nullable: true),
                    OutlineJson = table.Column<string>(type: "text", nullable: true),
                    ScenesJson = table.Column<string>(type: "text", nullable: true),
                    NpcsJson = table.Column<string>(type: "text", nullable: true),
                    EncountersJson = table.Column<string>(type: "text", nullable: true),
                    TreasureJson = table.Column<string>(type: "text", nullable: true),
                    CluesJson = table.Column<string>(type: "text", nullable: true),
                    Ruleset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GameSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedAdventures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameSystemDefinitionVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameSystemDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", maxLength: 2147483647, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSystemDefinitionVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSystemDefinitionVersions_GameSystemDefinitions_GameSyst~",
                        column: x => x.GameSystemDefinitionId,
                        principalTable: "GameSystemDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_IsBuiltIn_GameSystem",
                table: "SourceDocuments",
                columns: new[] { "IsBuiltIn", "GameSystem" });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_GameSystemDefinitionId",
                table: "Characters",
                column: "GameSystemDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_GameSystemDefinitionId",
                table: "Campaigns",
                column: "GameSystemDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_GameSystemDefinitionVersionId",
                table: "Campaigns",
                column: "GameSystemDefinitionVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AiActionProposals_CampaignId",
                table: "AiActionProposals",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_AiActionProposals_SessionId_Status",
                table: "AiActionProposals",
                columns: new[] { "SessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GameStateSnapshots_CampaignId",
                table: "GameStateSnapshots",
                column: "CampaignId",
                unique: true);

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
                name: "IX_GeneratedAdventures_Status",
                table: "GeneratedAdventures",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_GameSystemDefinitionVersions_GameSystemDefinition~",
                table: "Campaigns",
                column: "GameSystemDefinitionVersionId",
                principalTable: "GameSystemDefinitionVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_GameSystemDefinitions_GameSystemDefinitionId",
                table: "Campaigns",
                column: "GameSystemDefinitionId",
                principalTable: "GameSystemDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

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
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_GameSystemDefinitionVersions_GameSystemDefinition~",
                table: "Campaigns");

            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_GameSystemDefinitions_GameSystemDefinitionId",
                table: "Campaigns");

            migrationBuilder.DropForeignKey(
                name: "FK_Characters_GameSystemDefinitions_GameSystemDefinitionId",
                table: "Characters");

            migrationBuilder.DropTable(
                name: "AiActionProposals");

            migrationBuilder.DropTable(
                name: "GameStateSnapshots");

            migrationBuilder.DropTable(
                name: "GameSystemDefinitionVersions");

            migrationBuilder.DropTable(
                name: "GeneratedAdventures");

            migrationBuilder.DropTable(
                name: "GameSystemDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_SourceDocuments_IsBuiltIn_GameSystem",
                table: "SourceDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Characters_GameSystemDefinitionId",
                table: "Characters");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_GameSystemDefinitionId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_GameSystemDefinitionVersionId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "AttributionText",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "AttributionUrl",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "IsBuiltIn",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "IsDisabled",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "LicenseDisplayName",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "LicenseKey",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "GameSystemDefinitionId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "GameSystemDefinitionId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "GameSystemDefinitionVersionId",
                table: "Campaigns");

            migrationBuilder.AlterColumn<string>(
                name: "Formula",
                table: "Rolls",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            // Reverse of the manually-added Tags/Summary columns (see Up()).
            migrationBuilder.DropColumn(
                name: "Tags",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Sessions");
        }
    }
}
