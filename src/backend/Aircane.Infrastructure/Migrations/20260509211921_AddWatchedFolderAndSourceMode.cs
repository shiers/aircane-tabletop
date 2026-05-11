using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchedFolderAndSourceMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StoragePath",
                table: "SourceDocuments",
                newName: "SourcePath");

            migrationBuilder.AddColumn<bool>(
                name: "IsSourceAvailable",
                table: "SourceDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SourceMode",
                table: "SourceDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WatchedFolderId",
                table: "SourceDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WatchedFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AbsolutePath = table.Column<string>(type: "text", nullable: false),
                    DefaultSourceType = table.Column<int>(type: "integer", nullable: false),
                    DefaultGameSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DefaultRuleset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedFolders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_WatchedFolderId",
                table: "SourceDocuments",
                column: "WatchedFolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_SourceDocuments_WatchedFolders_WatchedFolderId",
                table: "SourceDocuments",
                column: "WatchedFolderId",
                principalTable: "WatchedFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SourceDocuments_WatchedFolders_WatchedFolderId",
                table: "SourceDocuments");

            migrationBuilder.DropTable(
                name: "WatchedFolders");

            migrationBuilder.DropIndex(
                name: "IX_SourceDocuments_WatchedFolderId",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "IsSourceAvailable",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "SourceMode",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "WatchedFolderId",
                table: "SourceDocuments");

            migrationBuilder.RenameColumn(
                name: "SourcePath",
                table: "SourceDocuments",
                newName: "StoragePath");
        }
    }
}
