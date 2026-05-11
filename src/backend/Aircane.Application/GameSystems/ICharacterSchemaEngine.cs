using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Engine for validating character data, computing calculated fields,
/// generating form descriptors, and mapping imported fields against a Character Schema.
/// </summary>
public interface ICharacterSchemaEngine
{
    /// <summary>
    /// Validates character JSON against the active Character Schema.
    /// </summary>
    /// <param name="characterJson">The character data as a JSON string.</param>
    /// <param name="schema">The character schema to validate against.</param>
    /// <returns>A validation result with field-path errors.</returns>
    CharacterValidationResult Validate(string characterJson, CharacterSchema schema);

    /// <summary>
    /// Computes calculated fields based on source field values.
    /// </summary>
    /// <param name="characterJson">The character data as a JSON string.</param>
    /// <param name="schema">The character schema containing calculated field definitions.</param>
    /// <returns>A dictionary of calculated field IDs to their computed values (null if computation fails).</returns>
    Dictionary<string, object?> ComputeCalculatedFields(string characterJson, CharacterSchema schema);

    /// <summary>
    /// Generates a UI form descriptor from the Character Schema for frontend rendering.
    /// </summary>
    /// <param name="schema">The character schema to generate a form descriptor from.</param>
    /// <returns>A FormDescriptor describing the form layout.</returns>
    FormDescriptor GenerateFormDescriptor(CharacterSchema schema);

    /// <summary>
    /// Attempts to map imported fields to the schema, returning mapped and unmapped fields.
    /// </summary>
    /// <param name="extractedFields">Fields extracted from an import source (key=field name, value=field value).</param>
    /// <param name="schema">The character schema to map against.</param>
    /// <returns>A result containing mapped and unmapped fields.</returns>
    FieldMappingResult MapImportedFields(Dictionary<string, string> extractedFields, CharacterSchema schema);
}
