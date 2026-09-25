namespace Aircane.Application.Abstractions;

/// <summary>
/// Reads verbatim license text files (the full OGL v1.0a license and its Section 15 attribution
/// chain) that ship as embedded resources alongside a built-in content bundle's manifest.
/// </summary>
/// <remarks>
/// Built-in documents store the bundle manifest's embedded-resource name in
/// <c>SourceDocument.SourcePath</c> (e.g. <c>"Aircane.Workers.Resources.builtin.pf1e_prd.manifest.json"</c>).
/// The license text files are siblings of that manifest in the resource namespace.
/// </remarks>
public interface IBuiltInLicenseTextReader
{
    /// <summary>
    /// Reads the full OGL v1.0a license text (<c>OGL-1.0a.txt</c>) that is a sibling of the
    /// bundle manifest named by <paramref name="manifestResourceName"/>.
    /// Returns null if the resource does not exist.
    /// </summary>
    Task<string?> ReadOglTextAsync(string manifestResourceName, CancellationToken ct = default);

    /// <summary>
    /// Reads the Section 15 attribution chain text (<c>SECTION-15.txt</c>) that is a sibling of
    /// the bundle manifest named by <paramref name="manifestResourceName"/>.
    /// Returns null if the resource does not exist.
    /// </summary>
    Task<string?> ReadSection15Async(string manifestResourceName, CancellationToken ct = default);
}
