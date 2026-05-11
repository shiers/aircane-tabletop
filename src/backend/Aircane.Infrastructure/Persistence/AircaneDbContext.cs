using Aircane.Domain.Entities;
using Aircane.Domain.Entities.GameSystems;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Aircane.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Aircane Tabletop application.
/// Configures all core domain entities against PostgreSQL.
/// </summary>
public class AircaneDbContext : DbContext
{
    private readonly bool _isInMemory;

    public AircaneDbContext(DbContextOptions<AircaneDbContext> options) : base(options)
    {
        // Detect in-memory provider at construction time to avoid accessing
        // Database.ProviderName during OnModelCreating (which triggers DI resolution too early).
        _isInMemory = options.Extensions.Any(e =>
            e.GetType().FullName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true);
    }

    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionParticipant> SessionParticipants => Set<SessionParticipant>();
    public DbSet<WatchedFolder> WatchedFolders => Set<WatchedFolder>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterTemplate> CharacterTemplates => Set<CharacterTemplate>();
    public DbSet<Roll> Rolls => Set<Roll>();
    public DbSet<CampaignEvent> CampaignEvents => Set<CampaignEvent>();
    public DbSet<GameStateSnapshot> GameStateSnapshots => Set<GameStateSnapshot>();
    public DbSet<AiActionProposal> AiActionProposals => Set<AiActionProposal>();
    public DbSet<GeneratedAdventure> GeneratedAdventures => Set<GeneratedAdventure>();
    public DbSet<GameSystemDefinition> GameSystemDefinitions => Set<GameSystemDefinition>();
    public DbSet<GameSystemDefinitionVersion> GameSystemDefinitionVersions => Set<GameSystemDefinitionVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension (PostgreSQL only)
        if (!_isInMemory)
            modelBuilder.HasPostgresExtension("vector");

        // ── Campaign ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.GameSystem).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Ruleset).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AiRole).HasConversion<int>();
            entity.Property(e => e.AiAuthority).HasConversion<int>();
            entity.Property(e => e.ActiveAdventureId).IsRequired(false);
            entity.Property(e => e.GameSystemDefinitionId).IsRequired(false);
            entity.Property(e => e.GameSystemDefinitionVersionId).IsRequired(false);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasOne(e => e.GameSystemDefinition)
                .WithMany()
                .HasForeignKey(e => e.GameSystemDefinitionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.GameSystemDefinitionVersion)
                .WithMany()
                .HasForeignKey(e => e.GameSystemDefinitionVersionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.GameSystemDefinitionId)
                .HasDatabaseName("IX_Campaigns_GameSystemDefinitionId");
        });

        // ── Session ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.AccessMode).HasConversion<int>();
            entity.Property(e => e.InviteCodeHash).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.StartedAt).IsRequired(false);
            entity.Property(e => e.EndedAt).IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.Summary).HasColumnType("text").IsRequired(false);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.CampaignId).HasDatabaseName("IX_Sessions_CampaignId");
        });

        // ── SessionParticipant ────────────────────────────────────────────────
        modelBuilder.Entity<SessionParticipant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Role).HasConversion<int>();
            entity.Property(e => e.CharacterId).IsRequired(false);
            entity.Property(e => e.JoinedAt).IsRequired();
            entity.Property(e => e.LastSeenAt).IsRequired();
            entity.Property(e => e.IsApproved).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.SessionId).HasDatabaseName("IX_SessionParticipants_SessionId");
        });

        // ── Character ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Character>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CampaignId).IsRequired(false);
            entity.Property(e => e.OwnerParticipantId).IsRequired(false);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.GameSystem).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Ruleset).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Level).IsRequired();
            entity.Property(e => e.GameSystemDefinitionId).IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.CanonicalJson).HasColumnType("text").IsRequired();
            if (!_isInMemory) entity.Property(e => e.CurrentStateJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasOne(e => e.GameSystemDefinition)
                .WithMany()
                .HasForeignKey(e => e.GameSystemDefinitionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.CampaignId).HasDatabaseName("IX_Characters_CampaignId");
            entity.HasIndex(e => e.GameSystemDefinitionId)
                .HasDatabaseName("IX_Characters_GameSystemDefinitionId");
        });

        // ── WatchedFolder ─────────────────────────────────────────────────────
        modelBuilder.Entity<WatchedFolder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DisplayName).HasMaxLength(300).IsRequired();
            if (!_isInMemory) entity.Property(e => e.AbsolutePath).HasColumnType("text").IsRequired();
            entity.Property(e => e.DefaultSourceType).HasConversion<int>();
            entity.Property(e => e.DefaultGameSystem).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.DefaultRuleset).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.LastScannedAt).IsRequired(false);
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // ── SourceDocument ────────────────────────────────────────────────────
        modelBuilder.Entity<SourceDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.OriginalFileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.SourceType).HasConversion<int>();
            entity.Property(e => e.SourceMode).HasConversion<int>();
            entity.Property(e => e.GameSystem).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Ruleset).HasMaxLength(100).IsRequired();
            if (!_isInMemory) entity.Property(e => e.SourcePath).HasColumnType("text").IsRequired();
            entity.Property(e => e.WatchedFolderId).IsRequired(false);
            entity.Property(e => e.IsSourceAvailable).IsRequired();
            entity.Property(e => e.Visibility).HasConversion<int>();
            entity.Property(e => e.ImportStatus).HasConversion<int>();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Tags: stored as jsonb in PostgreSQL; plain string conversion for in-memory testing
            var tagsProperty = entity.Property(e => e.Tags)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>())
                .IsRequired();
            if (!_isInMemory) tagsProperty.HasColumnType("jsonb");

            // Relationship: SourceDocument → WatchedFolder (many-to-one, optional)
            entity.HasOne(e => e.WatchedFolder)
                .WithMany(f => f.SourceDocuments)
                .HasForeignKey(e => e.WatchedFolderId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.WatchedFolderId).HasDatabaseName("IX_SourceDocuments_WatchedFolderId");
        });

        // ── DocumentChunk ─────────────────────────────────────────────────────
        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ChunkIndex).IsRequired();
            if (!_isInMemory) entity.Property(e => e.Text).HasColumnType("text").IsRequired();
            entity.Property(e => e.PageNumber).IsRequired(false);
            entity.Property(e => e.SectionTitle).HasMaxLength(500).IsRequired(false);
            entity.Property(e => e.ChunkType).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.Visibility).HasConversion<int>();
            if (!_isInMemory) entity.Property(e => e.MetadataJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Embedding: native Vector type via Pgvector.EntityFrameworkCore; HNSW index for cosine similarity
            if (!_isInMemory)
            {
                entity.Property(e => e.Embedding)
                    .HasColumnType("vector(768)")
                    .IsRequired(false);

                entity.HasIndex(e => e.Embedding)
                    .HasMethod("hnsw")
                    .HasOperators("vector_cosine_ops")
                    .HasDatabaseName("IX_DocumentChunks_Embedding_Cosine");
            }
            else
            {
                entity.Ignore(e => e.Embedding);
            }

            entity.HasIndex(e => e.SourceDocumentId).HasDatabaseName("IX_DocumentChunks_SourceDocumentId");
        });

        // ── CharacterTemplate ─────────────────────────────────────────────────
        modelBuilder.Entity<CharacterTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.GameSystem).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Ruleset).HasMaxLength(100).IsRequired();
            if (!_isInMemory) entity.Property(e => e.FieldMappingsJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasIndex(e => e.GameSystem).HasDatabaseName("IX_CharacterTemplates_GameSystem");
        });

        // ── Roll ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<Roll>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CharacterId).IsRequired(false);
            entity.Property(e => e.RollerParticipantId).IsRequired();
            entity.Property(e => e.Formula).HasMaxLength(200).IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.DieResultsJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.Modifier).IsRequired();
            entity.Property(e => e.Total).IsRequired();
            entity.Property(e => e.IsManual).IsRequired();
            entity.Property(e => e.Visibility).HasConversion<int>();
            if (!_isInMemory) entity.Property(e => e.Context).HasColumnType("text").IsRequired(false);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.SessionId).HasDatabaseName("IX_Rolls_SessionId");
        });

        // ── CampaignEvent ─────────────────────────────────────────────────────
        modelBuilder.Entity<CampaignEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SessionId).IsRequired(false);
            entity.Property(e => e.ActorType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ActorId).IsRequired(false);
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            if (!_isInMemory) entity.Property(e => e.PayloadJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.Reversible).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.CampaignId).HasDatabaseName("IX_CampaignEvents_CampaignId");
            entity.HasIndex(e => e.SessionId).HasDatabaseName("IX_CampaignEvents_SessionId");
        });

        // ── GameStateSnapshot ─────────────────────────────────────────────────
        modelBuilder.Entity<GameStateSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CampaignId).IsRequired();
            entity.Property(e => e.ActiveSessionId).IsRequired(false);
            entity.Property(e => e.CurrentSceneId).HasMaxLength(200).IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.StateJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // One snapshot per campaign (latest wins)
            entity.HasIndex(e => e.CampaignId)
                .IsUnique()
                .HasDatabaseName("IX_GameStateSnapshots_CampaignId");
        });

        // ── AiActionProposal ─────────────────────────────────────────────────
        modelBuilder.Entity<AiActionProposal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SessionId).IsRequired();
            entity.Property(e => e.CampaignId).IsRequired();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.ActionType).HasMaxLength(100).IsRequired();
            if (!_isInMemory) entity.Property(e => e.PayloadJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.Label).HasMaxLength(500).IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.Reason).HasColumnType("text").IsRequired(false);
            entity.Property(e => e.ResolvedAt).IsRequired(false);
            entity.Property(e => e.ResolvedBy).HasMaxLength(200).IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.RejectionReason).HasColumnType("text").IsRequired(false);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => new { e.SessionId, e.Status })
                .HasDatabaseName("IX_AiActionProposals_SessionId_Status");
            entity.HasIndex(e => e.CampaignId)
                .HasDatabaseName("IX_AiActionProposals_CampaignId");
        });

        // ── GeneratedAdventure ────────────────────────────────────────────────
        modelBuilder.Entity<GeneratedAdventure>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.Ruleset).HasMaxLength(100).IsRequired();
            entity.Property(e => e.GameSystem).HasMaxLength(100).IsRequired();
            if (!_isInMemory) entity.Property(e => e.RequestJson).HasColumnType("text").IsRequired();
            if (!_isInMemory) entity.Property(e => e.PartyAnalysisJson).HasColumnType("text").IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.PitchJson).HasColumnType("text").IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.OutlineJson).HasColumnType("text").IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.ScenesJson).HasColumnType("text").IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.NpcsJson).HasColumnType("text").IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.EncountersJson).HasColumnType("text").IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.TreasureJson).HasColumnType("text").IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.CluesJson).HasColumnType("text").IsRequired(false);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasIndex(e => e.Status).HasDatabaseName("IX_GeneratedAdventures_Status");
        });

        // ── GameSystemDefinition ──────────────────────────────────────────────
        modelBuilder.Entity<GameSystemDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Identifier).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Version).HasMaxLength(50).IsRequired();
            entity.Property(e => e.SchemaVersion).IsRequired();
            entity.Property(e => e.Publisher).HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.Genre).HasMaxLength(100).IsRequired(false);
            if (!_isInMemory) entity.Property(e => e.Description).HasColumnType("text").IsRequired(false);
            entity.Property(e => e.License).HasMaxLength(50).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.IsBuiltIn).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Store DefinitionJson as jsonb in PostgreSQL
            var definitionJsonProperty = entity.Property(e => e.DefinitionJson)
                .HasMaxLength(int.MaxValue)
                .IsRequired();
            if (!_isInMemory) definitionJsonProperty.HasColumnType("jsonb");

            // Ignore rich domain navigation properties — only persist the JSON blob
            entity.Ignore(e => e.Tags);
            entity.Ignore(e => e.DiceConventions);
            entity.Ignore(e => e.ResolutionRules);
            entity.Ignore(e => e.CharacterSchema);
            entity.Ignore(e => e.ConditionSet);
            entity.Ignore(e => e.ActionEconomy);
            entity.Ignore(e => e.EncounterBudget);
            entity.Ignore(e => e.AiGuidance);

            // Unique index on Identifier for slug-based lookups
            entity.HasIndex(e => e.Identifier)
                .IsUnique()
                .HasDatabaseName("IX_GameSystemDefinitions_Identifier");
        });

        // ── GameSystemDefinitionVersion ───────────────────────────────────────
        modelBuilder.Entity<GameSystemDefinitionVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GameSystemDefinitionId).IsRequired();
            entity.Property(e => e.Version).HasMaxLength(50).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Store DefinitionJson as jsonb in PostgreSQL
            var definitionJsonProperty = entity.Property(e => e.DefinitionJson)
                .HasMaxLength(int.MaxValue)
                .IsRequired();
            if (!_isInMemory) definitionJsonProperty.HasColumnType("jsonb");

            entity.HasOne(e => e.GameSystemDefinition)
                .WithMany()
                .HasForeignKey(e => e.GameSystemDefinitionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.GameSystemDefinitionId)
                .HasDatabaseName("IX_GameSystemDefinitionVersions_GameSystemDefinitionId");
        });
    }
}
