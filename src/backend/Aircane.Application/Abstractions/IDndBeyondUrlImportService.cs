namespace Aircane.Application.Abstractions;

/// <summary>
/// Fetches raw D&amp;D Beyond character JSON for a given character URL or id.
/// Implemented in Infrastructure (FEAT-003). The character URL is never stored or logged.
/// </summary>
public interface IDndBeyondUrlImportService
{
    /// <summary>
    /// Fetches the raw character JSON for the supplied D&amp;D Beyond character URL (or bare id).
    /// Throws <see cref="DndBeyondImportException"/> for user-correctable failures.
    /// </summary>
    Task<string> FetchAsync(string characterUrl, CancellationToken ct);
}

/// <summary>Categorises a D&amp;D Beyond import failure so the controller can map it to a status.</summary>
public enum DndBeyondImportStatusKind
{
    /// <summary>The character sheet is private (HTTP 403).</summary>
    Private403,

    /// <summary>The character could not be found (HTTP 404).</summary>
    NotFound404,

    /// <summary>The request timed out or was cancelled.</summary>
    Timeout,

    /// <summary>An upstream/network error occurred.</summary>
    Upstream,

    /// <summary>The supplied URL or id could not be parsed.</summary>
    InvalidUrl
}

/// <summary>
/// Raised when a D&amp;D Beyond URL import fails in a way the user can act on. Carries a
/// <see cref="DndBeyondImportStatusKind"/>; never includes the character URL in its message.
/// </summary>
public sealed class DndBeyondImportException : Exception
{
    public DndBeyondImportException(DndBeyondImportStatusKind statusKind, string message)
        : base(message)
    {
        StatusKind = statusKind;
    }

    public DndBeyondImportException(DndBeyondImportStatusKind statusKind, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusKind = statusKind;
    }

    /// <summary>The category of failure.</summary>
    public DndBeyondImportStatusKind StatusKind { get; }
}
