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
/// licenses (CC BY 4.0, ORC, OGL v1.0a) require that attribution and license text be accessible to
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

    /// <summary>
    /// Returns the verbatim OGL v1.0a license text for an OGL built-in document.
    /// </summary>
    [HttpGet("{documentId:guid}/ogl-text")]
    [Produces("text/plain")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOglText(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var text = await _licenseService.GetOglTextAsync(documentId, cancellationToken);
        if (text is null)
            return NotFound(); // body-less 404 avoids conflicting with [Produces("text/plain")]

        return Content(text, "text/plain");
    }

    /// <summary>
    /// Returns the verbatim Section 15 attribution chain for an OGL built-in document.
    /// </summary>
    [HttpGet("{documentId:guid}/section-15")]
    [Produces("text/plain")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSection15(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var text = await _licenseService.GetSection15Async(documentId, cancellationToken);
        if (text is null)
            return NotFound(); // body-less 404 avoids conflicting with [Produces("text/plain")]

        return Content(text, "text/plain");
    }
}
