namespace Aircane.Domain.Enums;

/// <summary>
/// Discriminates how a source document was made available to the application.
/// FolderWatch: the host registered a local folder path; the app reads files directly from the filesystem.
/// Upload: the host uploaded a file; the app stores it in a managed location (future cloud/web mode).
/// Embedded: built-in content compiled into the assembly as an embedded resource (no filesystem dependency).
/// </summary>
public enum SourceMode
{
    FolderWatch = 0,
    Upload = 1,
    Embedded = 2
}
