using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Aircane.Api.Middleware;

/// <summary>
/// Middleware that blocks any HTTP request whose path appears to target a source document file.
/// <para>
/// Aircane Tabletop never serves source documents (PDFs, rulebooks, adventures) directly to clients.
/// Source files are read by the backend for indexing and RAG retrieval only. The API exposes
/// metadata through the LibraryController, but file content is never streamed or downloadable.
/// </para>
/// <para>
/// This middleware acts as a defense-in-depth measure: even if a misconfigured static file handler
/// or route were accidentally added, requests for common source document extensions would be blocked.
/// </para>
/// </summary>
public sealed class SourceFileAccessBlockerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SourceFileAccessBlockerMiddleware> _logger;

    /// <summary>
    /// File extensions that are never served directly. These correspond to source document types
    /// that the host imports into the library.
    /// </summary>
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".epub",
        ".mobi",
        ".djvu",
    };

    public SourceFileAccessBlockerMiddleware(RequestDelegate next, ILogger<SourceFileAccessBlockerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;

        if (!string.IsNullOrEmpty(path))
        {
            // Check if the request path ends with a blocked file extension.
            var extension = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(extension) && BlockedExtensions.Contains(extension))
            {
                _logger.LogWarning(
                    "Blocked direct source file access attempt. Path={RequestPath}, RemoteIp={RemoteIp}",
                    path,
                    context.Connection.RemoteIpAddress);

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Block path traversal attempts (e.g., /../../some/path/file.pdf encoded without extension)
            if (path.Contains("..", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Blocked path traversal attempt. Path={RequestPath}, RemoteIp={RemoteIp}",
                    path,
                    context.Connection.RemoteIpAddress);

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for registering the <see cref="SourceFileAccessBlockerMiddleware"/>.
/// </summary>
public static class SourceFileAccessBlockerMiddlewareExtensions
{
    /// <summary>
    /// Adds middleware that blocks direct HTTP access to source document files (PDFs, etc.).
    /// This is a defense-in-depth measure - the app never configures static file serving for
    /// user content directories, but this middleware ensures requests are rejected even if
    /// a misconfiguration occurs.
    /// </summary>
    public static IApplicationBuilder UseSourceFileAccessBlocker(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SourceFileAccessBlockerMiddleware>();
    }
}
