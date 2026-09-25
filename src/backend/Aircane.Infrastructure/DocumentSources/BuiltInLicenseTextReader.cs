using System.Reflection;
using Aircane.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.DocumentSources;

/// <summary>
/// Reads verbatim license text files embedded alongside a built-in bundle manifest, using the
/// assembly that carries the built-in content bundles (Aircane.Workers).
/// </summary>
public sealed class BuiltInLicenseTextReader : IBuiltInLicenseTextReader
{
    /// <summary>Conventional file name of the full OGL v1.0a license text within an OGL bundle.</summary>
    private const string OglTextFileName = "OGL-1.0a.txt";

    /// <summary>Conventional file name of the Section 15 attribution chain within an OGL bundle.</summary>
    private const string Section15FileName = "SECTION-15.txt";

    private readonly EmbeddedResourceDocumentSource _source;

    /// <summary>
    /// Creates a reader over the supplied assembly (the one carrying the built-in bundles).
    /// </summary>
    public BuiltInLicenseTextReader(
        Assembly contentAssembly,
        ILogger<EmbeddedResourceDocumentSource> sourceLogger)
    {
        _source = new EmbeddedResourceDocumentSource(contentAssembly, sourceLogger);
    }

    /// <inheritdoc />
    public Task<string?> ReadOglTextAsync(string manifestResourceName, CancellationToken ct = default)
        => ReadSiblingAsync(manifestResourceName, OglTextFileName, ct);

    /// <inheritdoc />
    public Task<string?> ReadSection15Async(string manifestResourceName, CancellationToken ct = default)
        => ReadSiblingAsync(manifestResourceName, Section15FileName, ct);

    private async Task<string?> ReadSiblingAsync(
        string manifestResourceName,
        string siblingFileName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(manifestResourceName))
            return null;

        var resourceName = ResolveSiblingResource(manifestResourceName, siblingFileName);

        if (!await _source.FileExistsAsync(resourceName, ct))
            return null;

        await using var stream = await _source.OpenStreamAsync(resourceName, ct);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Resolves the full resource name of a file that is a sibling of the manifest.
    /// For <c>"A.B.pf1e_prd.manifest.json"</c> + <c>"OGL-1.0a.txt"</c> returns
    /// <c>"A.B.pf1e_prd.OGL-1.0a.txt"</c>.
    /// </summary>
    private static string ResolveSiblingResource(string manifestResourceName, string siblingFileName)
    {
        const string manifestSuffix = ".manifest.json";
        var ns = manifestResourceName.EndsWith(manifestSuffix, StringComparison.OrdinalIgnoreCase)
            ? manifestResourceName[..^manifestSuffix.Length]
            : manifestResourceName;
        return $"{ns}.{siblingFileName}";
    }
}
