using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Applies sensible defaults to a GameSystemDefinition for any omitted optional sections.
/// Returns a new instance with defaults filled in — does not mutate the original.
/// </summary>
public interface IGameSystemDefaultsApplicator
{
    /// <summary>
    /// Returns a new GameSystemDefinition with defaults applied for any null/empty optional sections.
    /// </summary>
    GameSystemDefinition ApplyDefaults(GameSystemDefinition definition);
}
