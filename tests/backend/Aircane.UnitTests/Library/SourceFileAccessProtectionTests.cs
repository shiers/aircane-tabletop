using System.Reflection;
using Aircane.Api.Middleware;
using Aircane.Application.DTOs.Library;
using Aircane.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Verifies that source document metadata endpoints do not expose file paths or content,
/// and that the <see cref="SourceFileAccessBlockerMiddleware"/> blocks direct file access attempts.
/// </summary>
public class SourceFileAccessProtectionTests
{
    // ── SourceDocumentDto field safety ────────────────────────────────────────────

    [Fact]
    public void SourceDocumentDto_DoesNotExposeSourcePath()
    {
        // The DTO must never include a SourcePath, FilePath, StoragePath, or similar property
        // that would reveal where the file lives on the host's filesystem.
        var properties = typeof(SourceDocumentDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyNames = properties.Select(p => p.Name).ToList();

        Assert.DoesNotContain("SourcePath", propertyNames);
        Assert.DoesNotContain("FilePath", propertyNames);
        Assert.DoesNotContain("StoragePath", propertyNames);
        Assert.DoesNotContain("AbsolutePath", propertyNames);
        Assert.DoesNotContain("FileContent", propertyNames);
        Assert.DoesNotContain("Content", propertyNames);
        Assert.DoesNotContain("BinaryContent", propertyNames);
    }

    [Fact]
    public void SourceDocumentDto_OnlyContainsExpectedMetadataFields()
    {
        // Positive assertion: the DTO should contain only safe metadata fields.
        var properties = typeof(SourceDocumentDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyNames = properties.Select(p => p.Name).ToHashSet();

        var expectedFields = new HashSet<string>
        {
            "Id",
            "Title",
            "OriginalFileName",
            "SourceType",
            "SourceMode",
            "GameSystem",
            "Ruleset",
            "Visibility",
            "ImportStatus",
            "IsSourceAvailable",
            "WatchedFolderId",
            "CreatedAt",
            "UpdatedAt",
            "Tags",
        };

        Assert.Equal(expectedFields, propertyNames);
    }

    // ── SourceFileAccessBlockerMiddleware ─────────────────────────────────────────

    private static SourceFileAccessBlockerMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        var logger = NullLogger<SourceFileAccessBlockerMiddleware>.Instance;
        return new SourceFileAccessBlockerMiddleware(next, logger);
    }

    [Theory]
    [InlineData("/documents/rulebook.pdf")]
    [InlineData("/files/adventure.PDF")]
    [InlineData("/api/library/some-file.epub")]
    [InlineData("/content/module.mobi")]
    [InlineData("/scans/page.djvu")]
    public async Task Middleware_BlocksSourceFileExtensions(string path)
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/api/library/documents")]
    [InlineData("/api/library/documents/123")]
    [InlineData("/health")]
    [InlineData("/hubs/session")]
    [InlineData("/api/campaigns")]
    [InlineData("/api/characters/import")]
    public async Task Middleware_AllowsNormalApiRequests(string path)
    {
        bool nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled, $"Expected middleware to pass through for path: {path}");
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/../../etc/passwd")]
    [InlineData("/api/../../../secret")]
    [InlineData("/files/..%2F..%2Fpasswd")]
    public async Task Middleware_BlocksPathTraversalAttempts(string path)
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_AllowsJsonApiEndpoints()
    {
        // .json extension is NOT blocked because it's used for API content-type negotiation
        // and is not a source document format that needs protection.
        // Source JSON files (character imports) are handled through the API, not static paths.
        bool nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/characters/import";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
