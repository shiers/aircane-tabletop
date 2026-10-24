using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Aircane.Application.DTOs.Ai;
using Aircane.Application.DTOs.GameSystems;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.GameSystems.Seeds;
using Aircane.Infrastructure.Persistence;
using Aircane.Workers.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Definition ruleset labels and campaign-scoped rules retrieval through the token-validating pipeline
/// (the host is the local machine; anyone else needs a real participant token).
/// </summary>
public sealed class RetrievalScopeIntegrationTests : IClassFixture<DesktopModeWebApplicationFactory>
{
    private const string DndName = "Dungeons & Dragons 5th Edition (2014)";
    private static readonly IPAddress LanAddress = IPAddress.Parse("192.168.1.50");

    private readonly DesktopModeWebApplicationFactory _factory;

    public RetrievalScopeIntegrationTests(DesktopModeWebApplicationFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<GameSystemDefinitionSeeder>().SeedAsync().GetAwaiter().GetResult();
    }

    private HttpClient Host() => _factory.CreateClientFrom(IPAddress.Loopback);

    private async Task<Guid> SeedSelectorCampaignAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        // What campaign creation stores for a campaign made from the selector: the definition's name and version.
        var campaign = new Campaign("Selector campaign", DndName, "1.0.0", AiRole.FullDm, AiAuthority.FullSessionControl);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    private async Task SeedRulesDocAsync(string title, string ruleset, string text)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AircaneDbContext>();
        var doc = new SourceDocument(title, "rules.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", ruleset, "/rules.pdf");
        db.SourceDocuments.Add(doc);
        db.DocumentChunks.Add(new DocumentChunk(doc.Id, 0, text, pageNumber: 1, sectionTitle: null,
            chunkType: "paragraph", visibility: ContentVisibility.Public));
        await db.SaveChangesAsync();
    }

    private static MultipartFormDataContent DefinitionFile(string json)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new MultipartFormDataContent { { file, "file", "definition.json" } };
    }

    private static string DefinitionJson(string identifier, string? rulesetsJson) => $$"""
        {
          "schemaVersion": 1,
          "metadata": {
            "id": "{{identifier}}", "name": "System {{identifier}}", "version": "1.0.0", "license": "user-created"
            {{(rulesetsJson is null ? "" : $", \"rulesets\": {rulesetsJson}")}}
          }
        }
        """;

    // ── Definition DTO and import ─────────────────────────────────────────────

    [Fact]
    public async Task GetDefinition_ExposesTheRulesetLabels()
    {
        using var host = Host();

        var dnd = await host.GetFromJsonAsync<GameSystemDefinitionDto>($"/api/game-systems/{DnD5e2014Seed.DefinitionId}");
        var freeform = await host.GetFromJsonAsync<GameSystemDefinitionDto>($"/api/game-systems/{GenericFreeformSeed.DefinitionId}");

        Assert.Equal(new[] { "2014" }, dnd!.Rulesets);
        Assert.Empty(freeform!.Rulesets!);
    }

    [Fact]
    public async Task GetDefinition_FromTheLanWithoutAToken_IsStill401()
    {
        using var lan = _factory.CreateClientFrom(LanAddress);

        using var response = await lan.GetAsync($"/api/game-systems/{DnD5e2014Seed.DefinitionId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Import_CarriesTheRulesets()
    {
        using var host = Host();
        var id = $"import-{Guid.NewGuid():N}";

        using var response = await host.PostAsync("/api/game-systems/import",
            DefinitionFile(DefinitionJson(id, "[\"Core\", \"Revised\"]")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GameSystemDefinitionDto>();
        Assert.Equal(new[] { "Core", "Revised" }, dto!.Rulesets);
        Assert.Contains("\"rulesets\"", dto.DefinitionJson);
    }

    [Fact]
    public async Task Import_OfAnOldDefinitionWithoutRulesets_Succeeds_WithNone()
    {
        using var host = Host();

        using var response = await host.PostAsync("/api/game-systems/import",
            DefinitionFile(DefinitionJson($"old-{Guid.NewGuid():N}", rulesetsJson: null)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<GameSystemDefinitionDto>();
        Assert.Empty(dto!.Rulesets!);
    }

    [Fact]
    public async Task Import_WithTooManyRulesets_Is400()
    {
        using var host = Host();
        var eleven = "[" + string.Join(", ", Enumerable.Range(1, 11).Select(i => $"\"r{i}\"")) + "]";

        using var response = await host.PostAsync("/api/game-systems/import",
            DefinitionFile(DefinitionJson($"many-{Guid.NewGuid():N}", eleven)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Rules question for a campaign ─────────────────────────────────────────

    [Fact]
    public async Task RulesQuestion_ForASelectorCampaign_WithNoRulesetPicked_FindsTheCampaignsBooks()
    {
        var campaignId = await SeedSelectorCampaignAsync();
        var title = $"Quillback 2014 {Guid.NewGuid():N}";
        await SeedRulesDocAsync(title, "2014", "Quillback grapple rules: contest your Athletics.");
        using var host = Host();

        using var response = await host.PostAsJsonAsync("/api/ai/rules-question",
            new RulesQuestionRequest("quillback grapple", CampaignId: campaignId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RulesQuestionResponse>();
        Assert.True(result!.HasSourceSupport);
        Assert.Contains(result.Citations, c => c.SourceTitle == title);
    }

    [Fact]
    public async Task RulesQuestion_WithAUserPickedRuleset_IsUnchanged()
    {
        var campaignId = await SeedSelectorCampaignAsync();
        var title = $"Thornwick 2014 {Guid.NewGuid():N}";
        await SeedRulesDocAsync(title, "2014", "Thornwick grapple rules: contest your Athletics.");
        using var host = Host();

        // The user picked 2024: only 2024 books are searched, whatever the campaign says.
        using var response = await host.PostAsJsonAsync("/api/ai/rules-question",
            new RulesQuestionRequest("thornwick grapple", GameSystem: "D&D 5e", Ruleset: "2024", CampaignId: campaignId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RulesQuestionResponse>();
        Assert.False(result!.HasSourceSupport);
        Assert.DoesNotContain(result.Citations, c => c.SourceTitle == title);
    }
}
