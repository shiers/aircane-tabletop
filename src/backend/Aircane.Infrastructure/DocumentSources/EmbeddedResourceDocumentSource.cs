using System.Reflection;
using System.Text.Json;
using Aircane.Application.Abstractions;
using Aircane.Application.Library;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.DocumentSources;

/// <summary>
/// <see cref="IDocumentSource"/> implementation for built-in content compiled into an assembly
/// as embedded resources. Reads files via <see cref="Assembly.GetManifestResourceStream(string)"/>
/// with no filesystem dependency.
/// </summary>
/// <remarks>
/// The <c>folderPath</c> argument to <see cref="ListFilesAsync"/> is interpreted as the full
/// manifest resource name of a bundle's <c>manifest.json</c> (e.g.
/// <c>"Aircane.Workers.Resources.builtin.dnd5e_srd.manifest.json"</c>). The <c>sourcePath</c>
/// argument to <see cref="OpenStreamAsync"/> is the full manifest resource name of an individual
/// embedded file.
/// </remarks>
public sealed class EmbeddedResourceDocumentSource : IDocumentSource
{
    private readonly Assembly _assembly;
    private readonly ILogger<EmbeddedResourceDocumentSource> _logger;

    /// <summary>
    /// Creates a source that reads embedded resources from the supplied assembly.
    /// </summary>
    public EmbeddedResourceDocumentSource(
        Assembly assembly,
        ILogger<EmbeddedResourceDocumentSource> logger)
    {
        _assembly = assembly;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads the manifest resource named by <paramref name="folderPath"/> and returns one
    /// <see cref="DocumentSourceFile"/> per file listed in the manifest. The
    /// <see cref="DocumentSourceFile.SourcePath"/> of each entry is the full manifest resource
    /// name of the content file, resolved relative to the manifest's resource namespace.
    /// </remarks>
    public async Task<IReadOnlyList<DocumentSourceFile>> ListFilesAsync(
        string folderPath,
        CancellationToken ct = default)
    {
        var manifest = await ReadManifestAsync(folderPath, ct);
        if (manifest is null)
            return Array.Empty<DocumentSourceFile>();

        // Content files are siblings of the manifest in the resource namespace.
        var resourcePrefix = GetResourceNamespace(folderPath);

        var files = new List<DocumentSourceFile>(manifest.Files.Count);
        foreach (var file in manifest.Files)
        {
            var resourceName = $"{resourcePrefix}.{file.File}";
            long? size = null;
            if (_assembly.GetManifestResourceInfo(resourceName) is not null)
            {
                using var stream = _assembly.GetManifestResourceStream(resourceName);
                size = stream?.Length;
            }
            else
            {
                _logger.LogWarning(
                    "EmbeddedResourceDocumentSource: manifest references missing resource '{Resource}'.",
                    resourceName);
            }

            files.Add(new DocumentSourceFile(
                SourcePath: resourceName,
                FileName: file.File,
                LastModifiedUtc: null,
                SizeBytes: size));
        }

        return files;
    }

    /// <inheritdoc />
    public Task<Stream> OpenStreamAsync(string sourcePath, CancellationToken ct = default)
    {
        var stream = _assembly.GetManifestResourceStream(sourcePath)
            ?? throw new FileNotFoundException(
                $"Embedded resource not found: '{sourcePath}'.", sourcePath);

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<bool> FileExistsAsync(string sourcePath, CancellationToken ct = default)
    {
        var exists = _assembly.GetManifestResourceInfo(sourcePath) is not null;
        return Task.FromResult(exists);
    }

    /// <summary>
    /// Reads and deserializes the manifest resource named by <paramref name="manifestResourceName"/>.
    /// Returns null if the resource is missing or cannot be parsed.
    /// </summary>
    public async Task<BuiltInContentManifest?> ReadManifestAsync(
        string manifestResourceName,
        CancellationToken ct = default)
    {
        var stream = _assembly.GetManifestResourceStream(manifestResourceName);
        if (stream is null)
        {
            _logger.LogWarning(
                "EmbeddedResourceDocumentSource: manifest resource '{Resource}' not found.",
                manifestResourceName);
            return null;
        }

        await using (stream)
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<BuiltInContentManifest>(
                    stream,
                    (JsonSerializerOptions?)null,
                    ct);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "EmbeddedResourceDocumentSource: failed to parse manifest '{Resource}'.",
                    manifestResourceName);
                return null;
            }
        }
    }

    /// <summary>
    /// Derives the resource namespace (everything up to and including the folder segment) from a
    /// manifest resource name so that sibling files can be resolved. For
    /// <c>"A.B.builtin.dnd5e_srd.manifest.json"</c> this returns <c>"A.B.builtin.dnd5e_srd"</c>.
    /// </summary>
    private static string GetResourceNamespace(string manifestResourceName)
    {
        const string manifestSuffix = ".manifest.json";
        if (manifestResourceName.EndsWith(manifestSuffix, StringComparison.OrdinalIgnoreCase))
            return manifestResourceName[..^manifestSuffix.Length];

        // Fallback: strip the last two dot-segments (filename + extension).
        var lastDot = manifestResourceName.LastIndexOf('.');
        var secondLastDot = lastDot > 0 ? manifestResourceName.LastIndexOf('.', lastDot - 1) : -1;
        return secondLastDot > 0 ? manifestResourceName[..secondLastDot] : manifestResourceName;
    }
}
