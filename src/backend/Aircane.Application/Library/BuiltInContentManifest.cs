using System.Text.Json.Serialization;

namespace Aircane.Application.Library;

/// <summary>
/// Deserialized shape of a built-in content bundle's <c>manifest.json</c>.
/// Describes the license, classification, and file list of an embedded rules bundle.
/// </summary>
public sealed class BuiltInContentManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("gameSystem")]
    public string GameSystem { get; set; } = string.Empty;

    [JsonPropertyName("ruleset")]
    public string Ruleset { get; set; } = string.Empty;

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "Rules";

    [JsonPropertyName("licenseKey")]
    public string LicenseKey { get; set; } = string.Empty;

    [JsonPropertyName("licenseDisplayName")]
    public string LicenseDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("attributionText")]
    public string AttributionText { get; set; } = string.Empty;

    [JsonPropertyName("attributionUrl")]
    public string AttributionUrl { get; set; } = string.Empty;

    /// <summary>
    /// For OGL bundles: relative resource name of the SECTION-15.txt file that carries the
    /// full attribution chain. Null for non-OGL bundles.
    /// </summary>
    [JsonPropertyName("oglSection15File")]
    public string? OglSection15File { get; set; }

    /// <summary>
    /// For OGL bundles: list of Product Identity terms deliberately omitted from the content.
    /// </summary>
    [JsonPropertyName("productIdentityExclusions")]
    public List<string> ProductIdentityExclusions { get; set; } = [];

    [JsonPropertyName("files")]
    public List<BuiltInContentFile> Files { get; set; } = [];
}

/// <summary>
/// A single content file within a built-in bundle.
/// </summary>
public sealed class BuiltInContentFile
{
    /// <summary>Relative file name of the content file (e.g. "combat.md").</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    /// <summary>Chunk type hint for the indexing pipeline (e.g. "rules", "spell", "condition").</summary>
    [JsonPropertyName("chunkType")]
    public string? ChunkType { get; set; }

    /// <summary>Human-readable section title (e.g. "Combat", "Conditions").</summary>
    [JsonPropertyName("section")]
    public string? Section { get; set; }
}
