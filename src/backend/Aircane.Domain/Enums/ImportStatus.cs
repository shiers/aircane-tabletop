namespace Aircane.Domain.Enums;

/// <summary>
/// Tracks the processing state of a source document import job.
/// </summary>
public enum ImportStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    OcrRequired
}
