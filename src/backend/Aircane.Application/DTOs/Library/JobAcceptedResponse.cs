namespace Aircane.Application.DTOs.Library;

/// <summary>
/// Returned with HTTP 202 Accepted when a long-running operation is enqueued as a background
/// job. The client can correlate progress via <c>GET /api/jobs/{jobId}</c> and the
/// <c>ImportStatusUpdated</c> SignalR event (which carries the same job id).
/// </summary>
/// <param name="JobId">The id of the enqueued background job.</param>
/// <param name="JobType">A stable discriminator for the job type (e.g. "FolderScan", "ReembedAll").</param>
public sealed record JobAcceptedResponse(Guid JobId, string JobType);
