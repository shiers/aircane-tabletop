using System.Reflection;
using Aircane.Application.Abstractions;
using Aircane.Application.Library;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Domain.License;
using Aircane.Infrastructure.DocumentSources;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Workers.Seeding;

/// <summary>
/// Seeds built-in open-content rules bundles (D&amp;D 5e SRD 5.1, Pathfinder 2e Remaster,
/// Pathfinder 1e PRD) from embedded resources into the database on startup.
/// Idempotent: an existing built-in document for a given (id) is skipped.
/// </summary>
public sealed class BuiltInContentSeeder
{
    /// <summary>
    /// Manifest resource names of the built-in bundles, in seeding order.
    /// Each corresponds to a subfolder under <c>Resources/builtin/</c> marked as EmbeddedResource.
    /// </summary>
    private static readonly string[] BundleManifests =
    {
        "Aircane.Workers.Resources.builtin.dnd5e_srd.manifest.json",
        "Aircane.Workers.Resources.builtin.pf2e_remaster.manifest.json",
        "Aircane.Workers.Resources.builtin.pf1e_prd.manifest.json"
    };

    /// <summary>
    /// Bundles that yield fewer than this many chunks are treated as incomplete: the seeder
    /// logs a warning but does not fail, so the app starts cleanly with a partial bundle.
    /// </summary>
    private const int MinUsefulChunkCount = 10;

    private readonly AircaneDbContext _db;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ITextChunker _chunker;
    private readonly EmbeddedResourceDocumentSource _source;
    private readonly ILogger<BuiltInContentSeeder> _logger;

    public BuiltInContentSeeder(
        AircaneDbContext db,
        IEmbeddingProvider embeddingProvider,
        ITextChunker chunker,
        ILogger<BuiltInContentSeeder> logger,
        ILogger<EmbeddedResourceDocumentSource> sourceLogger)
    {
        _db = db;
        _embeddingProvider = embeddingProvider;
        _chunker = chunker;
        _logger = logger;
        _source = new EmbeddedResourceDocumentSource(typeof(BuiltInContentSeeder).Assembly, sourceLogger);
    }

    /// <summary>
    /// Seeds all built-in bundles. Safe to call on every startup; already-seeded bundles are skipped.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var manifestResource in BundleManifests)
        {
            try
            {
                await SeedBundleAsync(manifestResource, ct);
            }
            catch (Exception ex)
            {
                // A single failing bundle must not block the others or app startup.
                _logger.LogError(ex,
                    "BuiltInContentSeeder: failed to seed bundle '{Manifest}'.", manifestResource);
            }
        }
    }

    private async Task SeedBundleAsync(string manifestResource, CancellationToken ct)
    {
        var manifest = await _source.ReadManifestAsync(manifestResource, ct);
        if (manifest is null)
        {
            _logger.LogWarning(
                "BuiltInContentSeeder: bundle '{Manifest}' not found or unparsable; skipping.",
                manifestResource);
            return;
        }

        // OGL bundles must carry a Section 15 attribution chain.
        if (BuiltInLicenses.IsOgl(manifest.LicenseKey))
        {
            if (string.IsNullOrWhiteSpace(manifest.OglSection15File))
            {
                _logger.LogError(
                    "BuiltInContentSeeder: OGL bundle '{Id}' is missing oglSection15File; refusing to seed.",
                    manifest.Id);
                return;
            }

            var section15Resource = ResolveSiblingResource(manifestResource, manifest.OglSection15File);
            if (!await _source.FileExistsAsync(section15Resource, ct))
            {
                _logger.LogError(
                    "BuiltInContentSeeder: OGL bundle '{Id}' Section 15 resource '{Resource}' missing; refusing to seed.",
                    manifest.Id, section15Resource);
                return;
            }
        }

        // Idempotency: a built-in document for this bundle title already exists.
        var alreadySeeded = await _db.SourceDocuments
            .AnyAsync(d => d.IsBuiltIn && d.GameSystem == manifest.GameSystem && d.Title == manifest.DisplayName, ct);

        if (alreadySeeded)
        {
            _logger.LogInformation(
                "BuiltInContentSeeder: bundle '{Id}' already seeded; skipping.", manifest.Id);
            return;
        }

        var sourceType = Enum.TryParse<SourceType>(manifest.SourceType, ignoreCase: true, out var st)
            ? st
            : SourceType.Rules;

        var document = new SourceDocument(
            title: manifest.DisplayName,
            originalFileName: manifest.Id,
            sourceType: sourceType,
            sourceMode: SourceMode.Embedded,
            gameSystem: manifest.GameSystem,
            ruleset: manifest.Ruleset,
            sourcePath: manifestResource,
            watchedFolderId: null,
            // Built-in rules are public so players can look them up.
            visibility: ContentVisibility.Public,
            importStatus: ImportStatus.Completed,
            tags: new List<string> { manifest.Ruleset })
        {
            IsBuiltIn = true,
            IsDisabled = false,
            LicenseKey = manifest.LicenseKey,
            LicenseDisplayName = manifest.LicenseDisplayName,
            AttributionText = manifest.AttributionText,
            AttributionUrl = manifest.AttributionUrl,
        };

        _db.SourceDocuments.Add(document);
        await _db.SaveChangesAsync(ct);

        var files = await _source.ListFilesAsync(manifestResource, ct);
        var chunkCount = 0;

        foreach (var manifestFile in manifest.Files)
        {
            var file = files.FirstOrDefault(f => f.FileName == manifestFile.File);
            if (file is null)
            {
                // A file declared in the manifest is missing from the embedded resources.
                // Log a clear, actionable error and continue so the rest of the bundle still seeds.
                _logger.LogError(
                    "BuiltInContentSeeder: bundle '{Id}' declares file '{File}' in its manifest, " +
                    "but no matching embedded resource was found; skipping that file.",
                    manifest.Id, manifestFile.File);
                continue;
            }

            string text;
            await using (var stream = await _source.OpenStreamAsync(file.SourcePath, ct))
            using (var reader = new StreamReader(stream))
            {
                text = await reader.ReadToEndAsync(ct);
            }

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var textChunks = _chunker.Chunk(
                text,
                pageNumber: 1,
                startingChunkIndex: chunkCount,
                sectionTitle: manifestFile.Section);

            foreach (var tc in textChunks)
            {
                var chunk = new DocumentChunk(
                    sourceDocumentId: document.Id,
                    chunkIndex: tc.ChunkIndex,
                    text: tc.Text,
                    pageNumber: tc.PageNumber,
                    sectionTitle: tc.SectionTitle,
                    chunkType: manifestFile.ChunkType,
                    visibility: ContentVisibility.Public,
                    metadataJson: "{}");

                var embedding = await _embeddingProvider.GenerateEmbeddingAsync(tc.Text, ct);
                chunk.Embedding = new Pgvector.Vector(embedding);
                chunk.EmbeddingProvider = _embeddingProvider.ProviderName;
                chunk.EmbeddingModel = _embeddingProvider.ModelName;
                chunk.EmbeddingDimensions = embedding.Length;

                _db.DocumentChunks.Add(chunk);
                chunkCount++;
            }
        }

        await _db.SaveChangesAsync(ct);

        // A bundle that produces very few chunks is almost certainly incomplete (e.g. the
        // PF2e Remaster bundle is a work-in-progress pending ORC content extraction). This
        // is not an error — the app must run fine with a partial bundle — so warn rather
        // than throw, and still leave the document seeded with whatever content exists.
        if (chunkCount < MinUsefulChunkCount)
        {
            _logger.LogWarning(
                "BuiltInContentSeeder: bundle '{Id}' produced only {Chunks} chunk(s) " +
                "(minimum useful is {Min}) — bundle is incomplete, continuing.",
                manifest.Id, chunkCount, MinUsefulChunkCount);
        }
        else
        {
            _logger.LogInformation(
                "BuiltInContentSeeder: seeded bundle '{Id}' ({Chunks} chunks) under license {License}.",
                manifest.Id, chunkCount, manifest.LicenseKey);
        }
    }

    /// <summary>
    /// Resolves the full resource name of a file that is a sibling of the manifest.
    /// </summary>
    private static string ResolveSiblingResource(string manifestResource, string siblingFileName)
    {
        const string manifestSuffix = ".manifest.json";
        var ns = manifestResource.EndsWith(manifestSuffix, StringComparison.OrdinalIgnoreCase)
            ? manifestResource[..^manifestSuffix.Length]
            : manifestResource;
        return $"{ns}.{siblingFileName}";
    }
}
