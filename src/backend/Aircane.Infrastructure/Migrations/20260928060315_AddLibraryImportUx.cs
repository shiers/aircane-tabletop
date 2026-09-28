using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryImportUx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExcludePatterns",
                table: "WatchedFolders",
                type: "jsonb",
                nullable: false,
                // Default to an empty JSON array; existing folders get an empty exclude list until edited.
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "SourceDocuments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GameSystemAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameSystemDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Alias = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSystemAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSystemAliases_GameSystemDefinitions_GameSystemDefinitio~",
                        column: x => x.GameSystemDefinitionId,
                        principalTable: "GameSystemDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_ContentHash",
                table: "SourceDocuments",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_GameSystemAliases_GameSystemDefinitionId",
                table: "GameSystemAliases",
                column: "GameSystemDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSystemAliases");

            migrationBuilder.DropIndex(
                name: "IX_SourceDocuments_ContentHash",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "ExcludePatterns",
                table: "WatchedFolders");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "SourceDocuments");
        }
    }
}
