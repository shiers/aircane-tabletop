using Aircane.Application.DTOs.Retrieval;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Resolves the library scope rules retrieval should search for a campaign, so retrieval does not
/// depend on the campaign's free-text ruleset.
/// </summary>
public interface IRetrievalScopeResolver
{
    /// <summary>
    /// Resolves a campaign's game system to a definition, in this order: the bound
    /// <paramref name="gameSystemDefinitionId"/> when set; otherwise <paramref name="gameSystem"/>
    /// matched by name, identifier or alias. Returns null when neither resolves, in which case the
    /// caller filters by the campaign's own strings as before.
    /// </summary>
    Task<RetrievalScope?> ResolveAsync(
        Guid? gameSystemDefinitionId,
        string? gameSystem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the campaign and resolves its scope as <see cref="ResolveAsync"/> does. Returns null when
    /// the campaign does not exist or does not resolve to a definition.
    /// </summary>
    Task<RetrievalScope?> ResolveForCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);
}
