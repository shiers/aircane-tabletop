using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using FluentValidation.Results;

namespace Aircane.UnitTests.TestDoubles;

/// <summary>
/// Test double for <see cref="ISystemRegistry"/> that behaves as if no game-system definition is
/// bound: <see cref="GetByCampaignAsync"/> throws, mirroring the real registry's behaviour for an
/// unbound campaign. Optionally returns a supplied definition instead.
/// </summary>
public sealed class UnboundSystemRegistry : ISystemRegistry
{
    private readonly GameSystemDefinition? _definition;

    public UnboundSystemRegistry(GameSystemDefinition? definition = null) => _definition = definition;

    public Task<GameSystemDefinition> GetByCampaignAsync(Guid campaignId, CancellationToken ct = default)
        => _definition is not null
            ? Task.FromResult(_definition)
            : throw new InvalidOperationException($"Campaign {campaignId} has no bound game-system definition.");

    public Task<GameSystemDefinition> GetByIdAsync(Guid definitionId, CancellationToken ct = default)
        => _definition is not null
            ? Task.FromResult(_definition)
            : throw new InvalidOperationException($"Definition {definitionId} not found.");

    public Task<IReadOnlyList<GameSystemDefinitionSummary>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GameSystemDefinitionSummary>>([]);

    public Task<GameSystemDefinition> ImportAsync(Stream definitionFile, string format, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<Stream> ExportAsync(Guid definitionId, string format, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<ValidationResult> ValidateAsync(Stream definitionFile, string format, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<Guid> CreateAsync(GameSystemDefinition definition, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task UpdateAsync(Guid definitionId, GameSystemDefinition definition, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task DeactivateAsync(Guid definitionId, CancellationToken ct = default)
        => throw new NotSupportedException();
}
