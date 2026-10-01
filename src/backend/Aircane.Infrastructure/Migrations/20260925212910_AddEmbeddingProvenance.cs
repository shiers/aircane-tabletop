using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmbeddingDimensions",
                table: "DocumentChunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "DocumentChunks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingProvider",
                table: "DocumentChunks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddingDimensions",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingProvider",
                table: "DocumentChunks");
        }
    }
}
