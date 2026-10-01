using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Campaigns;
using Aircane.Domain.Entities;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Campaigns;

/// <summary>
/// EF Core-backed implementation of <see cref="ICampaignService"/>.
/// </summary>
public sealed class CampaignService : ICampaignService
{
    private readonly AircaneDbContext _db;
    private readonly ILogger<CampaignService> _logger;

    public CampaignService(AircaneDbContext db, ILogger<CampaignService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CampaignDto> CreateCampaignAsync(
        CreateCampaignRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var campaign = new Campaign(
            name: request.Name,
            gameSystem: request.GameSystem,
            ruleset: request.Ruleset,
            aiRole: request.AiRole,
            aiAuthority: request.AiAuthority);

        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Campaign created: {CampaignId} '{Name}'", campaign.Id, campaign.Name);

        return ToDto(campaign);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CampaignDto>> ListCampaignsAsync(
        CancellationToken ct = default)
    {
        var campaigns = await _db.Campaigns
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return campaigns.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<CampaignDto?> GetCampaignAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        return campaign is null ? null : ToDto(campaign);
    }

    /// <inheritdoc />
    public async Task<CampaignDto> UpdateCampaignAsync(
        Guid id,
        UpdateCampaignRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Campaign '{id}' not found.");

        if (request.Name is not null)
            campaign.Name = request.Name;

        if (request.AiRole.HasValue)
            campaign.AiRole = request.AiRole.Value;

        if (request.AiAuthority.HasValue)
            campaign.AiAuthority = request.AiAuthority.Value;

        if (request.ActiveAdventureId.HasValue)
            campaign.ActiveAdventureId = request.ActiveAdventureId.Value;

        campaign.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Campaign updated: {CampaignId}", campaign.Id);

        return ToDto(campaign);
    }

    /// <inheritdoc />
    public async Task DeleteCampaignAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Campaign '{id}' not found.");

        _db.Campaigns.Remove(campaign);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Campaign deleted: {CampaignId}", id);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static CampaignDto ToDto(Campaign c) => new(
        Id: c.Id,
        Name: c.Name,
        GameSystem: c.GameSystem,
        Ruleset: c.Ruleset,
        AiRole: c.AiRole,
        AiAuthority: c.AiAuthority,
        ActiveAdventureId: c.ActiveAdventureId,
        CreatedAt: c.CreatedAt,
        UpdatedAt: c.UpdatedAt);
}
