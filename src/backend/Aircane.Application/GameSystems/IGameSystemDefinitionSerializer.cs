using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Parses and serializes Game System Definition JSON documents.
/// </summary>
public interface IGameSystemDefinitionSerializer
{
    /// <summary>Parses JSON into a GameSystemDefinition. Returns errors on failure.</summary>
    GameSystemDefinitionParseResult ParseJson(string json);

    /// <summary>Parses JSON from a stream.</summary>
    GameSystemDefinitionParseResult ParseJson(Stream stream);

    /// <summary>Serializes a GameSystemDefinition to a valid JSON string matching the expected format.</summary>
    string PrintJson(GameSystemDefinition definition);
}
