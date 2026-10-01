using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aircane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPgvectorEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ensure the pgvector extension is available
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            // Add the embedding column as a vector(768) type
            migrationBuilder.AddColumn<string>(
                name: "Embedding",
                table: "DocumentChunks",
                type: "vector(768)",
                nullable: true);

            // Create HNSW index for cosine similarity search
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_DocumentChunks_Embedding_Cosine\" " +
                "ON \"DocumentChunks\" USING hnsw (\"Embedding\" vector_cosine_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_Embedding_Cosine",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "DocumentChunks");
        }
    }
}
