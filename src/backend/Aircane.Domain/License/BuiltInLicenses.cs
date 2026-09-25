namespace Aircane.Domain.License;

/// <summary>
/// Describes an open-content license under which built-in rules text is distributed.
/// </summary>
/// <param name="Key">Machine-readable license key (stored on <see cref="Entities.SourceDocument.LicenseKey"/>).</param>
/// <param name="DisplayName">Human-readable license name for UI display.</param>
/// <param name="Url">Canonical URL of the license text.</param>
/// <param name="RequiresAttributionDisplay">
/// True when the license obliges us to visibly display attribution to end users
/// (CC BY, OGL Section 15, ORC attribution notice).
/// </param>
public sealed record BuiltInLicense(
    string Key,
    string DisplayName,
    string Url,
    bool RequiresAttributionDisplay);

/// <summary>
/// Typed constants for the open-content licenses used by the built-in rules bundle.
/// These are library-content licenses on rules text (<see cref="Entities.SourceDocument"/>),
/// distinct from Game System Definition mechanic definitions.
/// </summary>
public static class BuiltInLicenses
{
    /// <summary>Creative Commons Attribution 4.0 International (D&amp;D 5e SRD 5.1).</summary>
    public const string CcBy40Key = "cc-by-4.0";

    /// <summary>Open RPG Creative License (Pathfinder 2e Remaster).</summary>
    public const string OrcKey = "orc";

    /// <summary>Open Game License v1.0a (Pathfinder 1e PRD).</summary>
    public const string Ogl10aKey = "ogl-1.0a";

    public static readonly BuiltInLicense CcBy40 = new(
        Key: CcBy40Key,
        DisplayName: "Creative Commons Attribution 4.0 International",
        Url: "https://creativecommons.org/licenses/by/4.0/",
        RequiresAttributionDisplay: true);

    public static readonly BuiltInLicense Orc = new(
        Key: OrcKey,
        DisplayName: "ORC License",
        Url: "https://paizo.com/orclicense",
        RequiresAttributionDisplay: true);

    public static readonly BuiltInLicense Ogl10a = new(
        Key: Ogl10aKey,
        DisplayName: "Open Game License v1.0a",
        Url: "https://opengamingfoundation.org/ogl.html",
        RequiresAttributionDisplay: true);

    private static readonly IReadOnlyDictionary<string, BuiltInLicense> ByKey =
        new Dictionary<string, BuiltInLicense>(StringComparer.OrdinalIgnoreCase)
        {
            [CcBy40Key] = CcBy40,
            [OrcKey] = Orc,
            [Ogl10aKey] = Ogl10a
        };

    /// <summary>All known built-in licenses.</summary>
    public static IReadOnlyCollection<BuiltInLicense> All => (IReadOnlyCollection<BuiltInLicense>)ByKey.Values;

    /// <summary>Resolves a license by its key, or null if the key is unknown.</summary>
    public static BuiltInLicense? TryResolve(string? key)
        => key is not null && ByKey.TryGetValue(key, out var license) ? license : null;

    /// <summary>Returns true if the given key refers to an OGL license (which carries Section 15 obligations).</summary>
    public static bool IsOgl(string? key)
        => string.Equals(key, Ogl10aKey, StringComparison.OrdinalIgnoreCase);
}
