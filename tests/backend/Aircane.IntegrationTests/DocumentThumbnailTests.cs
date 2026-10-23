using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Application.DTOs.Library;
using Aircane.Application.DTOs.Sessions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Choosing a document's thumbnail through the token-validating pipeline (desktop mode: the host is the
/// local machine, a player carries a real participant token): upload with a key, PATCH to set and clear,
/// and who may.
/// </summary>
public sealed class DocumentThumbnailTests : IClassFixture<DesktopModeWebApplicationFactory>
{
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");

    private readonly DesktopModeWebApplicationFactory _factory;

    public DocumentThumbnailTests(DesktopModeWebApplicationFactory factory) => _factory = factory;

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private static MultipartFormDataContent UploadForm(string? thumbnailKey, bool includeKeyField = true)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.4 thumbnail test"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "thumb-test.pdf");
        content.Add(new StringContent("Thumb Test"), "title");
        content.Add(new StringContent("0"), "sourceType");
        content.Add(new StringContent("D&D 5e 2014"), "gameSystem");
        content.Add(new StringContent("PHB"), "ruleset");
        if (includeKeyField)
            content.Add(new StringContent(thumbnailKey ?? string.Empty), "thumbnailKey");
        return content;
    }

    private async Task<Guid> SeedDocumentAsync(bool builtIn = false, string? key = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var doc = new SourceDocument(
            "Seeded", "seeded.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e 2014", "PHB", "storage/seeded")
        {
            IsBuiltIn = builtIn,
            ThumbnailKey = key,
        };
        db.SourceDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private async Task<string?> StoredKeyAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        return (await db.SourceDocuments.AsNoTracking().SingleAsync(d => d.Id == id)).ThumbnailKey;
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, string? key) =>
        client.PatchAsJsonAsync($"/api/library/documents/{id}/thumbnail", new { thumbnailKey = key });

    // ── Upload ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_WithAKey_StoresIt_AndTheListShowsIt()
    {
        using var host = Host();

        var response = await host.PostAsync("/api/library/documents", UploadForm("fantasy-knight-pf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<SourceDocumentDto>())!;
        Assert.Equal("fantasy-knight-pf", dto.ThumbnailKey);
        Assert.Equal("fantasy-knight-pf", await StoredKeyAsync(dto.Id));

        var list = (await host.GetFromJsonAsync<List<SourceDocumentDto>>("/api/library/documents"))!;
        Assert.Equal("fantasy-knight-pf", list.Single(d => d.Id == dto.Id).ThumbnailKey);
    }

    [Theory]
    [InlineData(false)]   // field omitted
    [InlineData(true)]    // field present but empty
    public async Task Upload_WithoutAKey_LeavesItNull(bool sendEmptyField)
    {
        using var host = Host();

        var response = await host.PostAsync(
            "/api/library/documents", UploadForm(thumbnailKey: null, includeKeyField: sendEmptyField));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<SourceDocumentDto>())!;
        Assert.Null(dto.ThumbnailKey);
        Assert.Null(await StoredKeyAsync(dto.Id));
    }

    [Theory]
    [InlineData("Fantasy Knight")]
    [InlineData("../secret")]
    [InlineData("double--hyphen")]
    [InlineData("UPPER")]
    public async Task Upload_WithAnInvalidKey_Is400_WithAClearMessage(string key)
    {
        using var host = Host();

        var response = await host.PostAsync("/api/library/documents", UploadForm(key));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Thumbnail key", await response.Content.ReadAsStringAsync());
    }

    // ── PATCH ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Patch_SetsTheKey_ThenClearsIt()
    {
        var id = await SeedDocumentAsync();
        using var host = Host();

        var set = await PatchAsync(host, id, "space-opera-sw");
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("space-opera-sw", (await set.Content.ReadFromJsonAsync<SourceDocumentDto>())!.ThumbnailKey);
        Assert.Equal("space-opera-sw", await StoredKeyAsync(id));

        var clear = await PatchAsync(host, id, null);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Null((await clear.Content.ReadFromJsonAsync<SourceDocumentDto>())!.ThumbnailKey);
        Assert.Null(await StoredKeyAsync(id));
    }

    [Fact]
    public async Task Patch_WithAnEmptyString_ClearsTheKey()
    {
        var id = await SeedDocumentAsync(key: "generic");
        using var host = Host();

        var response = await PatchAsync(host, id, "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await StoredKeyAsync(id));
    }

    [Theory]
    [InlineData("Not A Key")]
    [InlineData("trailing-")]
    [InlineData("a/b")]
    public async Task Patch_WithAnInvalidKey_Is400_AndKeepsTheOldKey(string key)
    {
        var id = await SeedDocumentAsync(key: "generic");
        using var host = Host();

        var response = await PatchAsync(host, id, key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Thumbnail key", await response.Content.ReadAsStringAsync());
        Assert.Equal("generic", await StoredKeyAsync(id));
    }

    [Fact]
    public async Task Patch_ForAnUnknownDocument_Is404()
    {
        using var host = Host();

        var response = await PatchAsync(host, Guid.NewGuid(), "generic");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WorksOnBuiltInDocuments()
    {
        var id = await SeedDocumentAsync(builtIn: true);
        using var host = Host();

        var response = await PatchAsync(host, id, "cavern-adventure-lm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<SourceDocumentDto>())!;
        Assert.True(dto.IsBuiltIn);
        Assert.Equal("cavern-adventure-lm", dto.ThumbnailKey);
    }

    // ── Who may ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task APlayer_CannotChangeAThumbnail()
    {
        var id = await SeedDocumentAsync(key: "generic");
        var token = await JoinAsPlayerAsync();
        using var player = _factory.CreateClientFrom(LanAddress);
        player.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await PatchAsync(player, id, "space-opera-sw");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("generic", await StoredKeyAsync(id));
    }

    [Fact]
    public async Task AnAnonymousCaller_FromTheNetwork_IsRefused_ForPatchAndUpload()
    {
        var id = await SeedDocumentAsync(key: "generic");
        using var stranger = _factory.CreateClientFrom(LanAddress);

        var patch = await PatchAsync(stranger, id, "space-opera-sw");
        var upload = await stranger.PostAsync("/api/library/documents", UploadForm("generic"));

        Assert.True(patch.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.True(upload.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Equal("generic", await StoredKeyAsync(id));
    }

    private async Task<string> JoinAsPlayerAsync()
    {
        using var host = Host();
        var campaign = (await (await host.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Thumb table", "D&D 5e 2014", "PHB")))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<CampaignDto>())!;
        var session = (await (await host.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/start-session",
            new { Name = "Table", AccessMode = (int)SessionAccessMode.LocalLan, RequireHostApproval = false }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionDto>())!;
        using var lan = _factory.CreateClientFrom(LanAddress);
        var joined = (await (await lan.PostAsJsonAsync($"/api/sessions/{session.Id}/join",
            new { DisplayName = "Alice", InviteCode = session.InviteCode })).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<JoinSessionResult>())!;
        return joined.ParticipantToken;
    }
}
