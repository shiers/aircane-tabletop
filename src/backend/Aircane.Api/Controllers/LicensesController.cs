using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// Publicly exposes license and attribution information for built-in rules content.
/// </summary>
/// <remarks>
/// This controller is intentionally <see cref="AllowAnonymousAttribute">anonymous</see>: open-content
/// licenses (CC BY 4.0, ORC) require that attribution and license text be accessible to
/// anyone consuming the content, so these endpoints must not sit behind authentication.
/// </remarks>
[ApiController]
[Route("api/library/licenses")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class LicensesController : ControllerBase
{
    private readonly ILicenseService _licenseService;

    public LicensesController(ILicenseService licenseService)
    {
        _licenseService = licenseService;
    }

    /// <summary>
    /// Lists license and attribution metadata for all built-in documents.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LicenseInfoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LicenseInfoDto>>> ListLicenses(
        CancellationToken cancellationToken)
    {
        var licenses = await _licenseService.ListBuiltInLicensesAsync(cancellationToken);
        return Ok(licenses);
    }
}
