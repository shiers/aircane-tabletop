namespace Aircane.Domain.Enums;

/// <summary>
/// Discriminates how a source document was made available to the application.
/// FolderWatch: the host registered a local folder path; the app reads files directly from the filesystem.
/// Upload: the host uploaded a file; the app stores it in a managed location (future cloud/web mode).
/// </summary>
public enum SourceMode
{
    FolderWatch,
    Upload
}
