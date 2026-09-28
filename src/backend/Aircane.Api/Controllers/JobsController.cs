using Aircane.Api.Authorization;
using Aircane.Application.Abstractions.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Exposes the status of background jobs (document import, folder scan, re-embed) so the host
/// UI can poll for progress or correlate the <c>ImportStatusUpdated</c> SignalR event to a job.
/// </summary>
[ApiController]
[Route("api/jobs")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.HostOnly)]
public sealed class JobsController : ControllerBase
{
    private readonly IBackgroundJobStatusStore _statusStore;

    public JobsController(IBackgroundJobStatusStore statusStore)
    {
        _statusStore = statusStore;
    }

    /// <summary>
    /// Returns the current status of a background job, or 404 if the job is unknown/evicted.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BackgroundJobStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetJob(Guid id)
    {
        var status = _statusStore.Get(id);
        return status is null ? NotFound() : Ok(status);
    }
}
