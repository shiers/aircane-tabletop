using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Retrieval;
using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aircane.Infrastructure.Retrieval;

/// <summary>
/// Resolves a campaign to the library scope its rules retrieval searches. A campaign stores its game
/// system and ruleset as free text, and a campaign created from a definition stores the definition's
/// version as its ruleset ("1.0.0"), which matches no document. When the campaign resolves to a
/// definition, the scope comes from the definition instead: its name, identifier and aliases for the
/// game system, and its declared ruleset labels (expanded through <see cref="RulesetNormalizer"/>) for
/// the ruleset. The campaign's own ruleset is then ignored.
/// </summary>
public sealed class RetrievalScopeResolver : IRetrievalScopeResolver
{
    private readonly AircaneDbContext _db;
    private readonly IGameSystemCanonicalizer _canonicalizer;
    private readonly IGameSystemDefinitionSerializer _serializer;
    private readonly ILogger<RetrievalScopeResolver> _logger;

    public RetrievalScopeResolver(
        AircaneDbContext db,
        IGameSystemCanonicalizer canonicalizer,
        IGameSystemDefinitionSerializer serializer,
        ILogger<RetrievalScopeResolver>? logger = null)
    {
        _db = db;
        _canonicalizer = canonicalizer;
        _serializer = serializer;
        _logger = logger ?? NullLogger<RetrievalScopeResolver>.Instance;
    }

    /// <inheritdoc />
    public async Task<RetrievalScope?> ResolveAsync(
        Guid? gameSystemDefinitionId,
        string? gameSystem,
        CancellationToken cancellationToken = default)
    {
        // 1. The bound definition, whether or not it is still active: a campaign keeps its system.
        if (gameSystemDefinitionId is { } definitionId && definitionId != Guid.Empty)
        {
            var bound = await _db.GameSystemDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
            if (bound is not null)
                return await BuildScopeAsync(bound, "bound definition", cancellationToken);
        }

        // 2. The campaign's game system text matched to an active definition by name, identifier or alias.
        if (!string.IsNullOrWhiteSpace(gameSystem))
        {
            var canonical = await _canonicalizer.CanonicalizeAsync(gameSystem, cancellationToken);
            var matched = await _db.GameSystemDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.IsActive && d.Name == canonical, cancellationToken);
            if (matched is not null)
                return await BuildScopeAsync(matched, "game system name", cancellationToken);
        }

        // 3. No definition: the caller filters by the campaign's own strings, as before.
        _logger.LogDebug("Retrieval scope not resolved for game system '{GameSystem}'; using the campaign's own strings.",
            gameSystem);
        return null;
    }

    /// <inheritdoc />
    public async Task<RetrievalScope?> ResolveForCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _db.Campaigns
            .AsNoTracking()
            .Where(c => c.Id == campaignId)
            .Select(c => new { c.GameSystem, c.GameSystemDefinitionId })
            .FirstOrDefaultAsync(cancellationToken);

        return campaign is null
            ? null
            : await ResolveAsync(campaign.GameSystemDefinitionId, campaign.GameSystem, cancellationToken);
    }

    private async Task<RetrievalScope> BuildScopeAsync(
        GameSystemDefinition definition,
        string source,
        CancellationToken cancellationToken)
    {
        var aliases = await _db.GameSystemAliases
            .AsNoTracking()
            .Where(a => a.GameSystemDefinitionId == definition.Id)
            .Select(a => a.Alias)
            .ToListAsync(cancellationToken);

        var gameSystems = new[] { definition.Name, definition.Identifier }
            .Concat(aliases)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var declared = ReadDeclaredRulesets(definition);
        var rulesets = declared
            .SelectMany(RulesetNormalizer.GetEquivalents)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        _logger.LogDebug(
            "Retrieval scope resolved from {Source}: definition {Identifier}, rulesets [{Rulesets}].",
            source, definition.Identifier, string.Join(", ", declared));

        return new RetrievalScope
        {
            DefinitionId = definition.Id,
            DefinitionIdentifier = definition.Identifier,
            DefinitionName = definition.Name,
            GameSystems = gameSystems,
            DeclaredRulesets = declared,
            Rulesets = rulesets,
        };
    }

    /// <summary>
    /// Reads the declared ruleset labels from the stored definition JSON (the entity only persists the
    /// JSON blob). An unreadable definition declares none, which means no ruleset filter.
    /// </summary>
    private IReadOnlyList<string> ReadDeclaredRulesets(GameSystemDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.DefinitionJson) || definition.DefinitionJson == "{}")
            return [];

        var parsed = _serializer.ParseJson(definition.DefinitionJson);
        if (!parsed.IsSuccess || parsed.Definition is null)
        {
            _logger.LogDebug("Definition {Identifier} could not be read; no ruleset filter applies.",
                definition.Identifier);
            return [];
        }

        return parsed.Definition.Rulesets
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();
    }
}
