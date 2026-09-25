namespace Aircane.Application.DTOs.Library;

/// <summary>
/// License and attribution metadata for a built-in rules content document.
/// Exposed publicly (no authentication) so license obligations can be fulfilled by any client.
/// </summary>
public sealed record LicenseInfoDto(
    /// <summary>The source document ID.</summary>
    Guid DocumentId,

    /// <summary>The source document title.</summary>
    string DocumentTitle,

    /// <summary>Machine-readable license key (e.g. "cc-by-4.0", "orc", "ogl-1.0a").</summary>
    string? LicenseKey,

    /// <summary>Human-readable license name.</summary>
    string? LicenseDisplayName,

    /// <summary>Canonical URL of the license text, if known.</summary>
    string? LicenseUrl,

    /// <summary>Full attribution notice text required by the license.</summary>
    string? AttributionText,

    /// <summary>Canonical source URL for attribution.</summary>
    string? AttributionUrl,

    /// <summary>True for built-in documents shipped with the app.</summary>
    bool IsBuiltIn,

    /// <summary>
    /// True when this document is under the OGL v1.0a and therefore has retrievable
    /// full-license-text and Section 15 attribution endpoints.
    /// </summary>
    bool IsOgl);
