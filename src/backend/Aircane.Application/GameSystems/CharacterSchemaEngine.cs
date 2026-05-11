using System.Text.Json;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Application.GameSystems;

/// <summary>
/// Engine for validating character data, computing calculated fields,
/// generating form descriptors, and mapping imported fields against a Character Schema.
/// </summary>
public class CharacterSchemaEngine : ICharacterSchemaEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public CharacterValidationResult Validate(string characterJson, CharacterSchema schema)
    {
        if (string.IsNullOrWhiteSpace(characterJson))
        {
            return new CharacterValidationResult
            {
                IsValid = false,
                Errors = [new CharacterValidationError { FieldPath = "", Message = "Character JSON is empty or null" }]
            };
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(characterJson);
        }
        catch (JsonException ex)
        {
            return new CharacterValidationResult
            {
                IsValid = false,
                Errors = [new CharacterValidationError { FieldPath = "", Message = $"Invalid JSON: {ex.Message}" }]
            };
        }

        var errors = new List<CharacterValidationError>();
        var root = doc.RootElement;

        foreach (var section in schema.Sections)
        {
            foreach (var field in section.Fields)
            {
                ValidateField(root, field, field.Id, errors);
            }
        }

        doc.Dispose();

        return new CharacterValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private static void ValidateField(JsonElement root, CharacterSchemaField field, string fieldPath, List<CharacterValidationError> errors)
    {
        var hasProperty = root.TryGetProperty(field.Id, out var element);

        // Check required fields
        if (field.Required)
        {
            if (!hasProperty || element.ValueKind == JsonValueKind.Null ||
                (element.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(element.GetString())))
            {
                errors.Add(new CharacterValidationError
                {
                    FieldPath = fieldPath,
                    Message = $"Field '{fieldPath}' is required"
                });
                return;
            }
        }

        // If field is not present and not required, skip further validation
        if (!hasProperty || element.ValueKind == JsonValueKind.Null)
            return;

        // Type-specific validation
        switch (field.Type)
        {
            case CharacterFieldType.Text:
                ValidateTextField(element, fieldPath, errors);
                break;
            case CharacterFieldType.Number:
                ValidateNumberField(element, field, fieldPath, errors);
                break;
            case CharacterFieldType.Boolean:
                ValidateBooleanField(element, fieldPath, errors);
                break;
            case CharacterFieldType.Enum:
                ValidateEnumField(element, field, fieldPath, errors);
                break;
            case CharacterFieldType.DiceExpression:
                ValidateDiceExpressionField(element, fieldPath, errors);
                break;
            case CharacterFieldType.List:
                ValidateListField(element, fieldPath, errors);
                break;
            case CharacterFieldType.Repeating:
                ValidateRepeatingField(element, field, fieldPath, errors);
                break;
            case CharacterFieldType.ResourcePool:
                ValidateResourcePoolField(element, field, fieldPath, errors);
                break;
            case CharacterFieldType.Calculated:
                // Calculated fields are read-only; no user input validation needed
                break;
            case CharacterFieldType.Grouped:
                // Grouped fields are structural; no specific validation
                break;
        }
    }

    private static void ValidateTextField(JsonElement element, string fieldPath, List<CharacterValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' must be a string"
            });
        }
    }

    private static void ValidateNumberField(JsonElement element, CharacterSchemaField field, string fieldPath, List<CharacterValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' must be a number"
            });
            return;
        }

        if (!element.TryGetDouble(out var value))
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' has an invalid numeric value"
            });
            return;
        }

        if (field.Min.HasValue && value < field.Min.Value)
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' must be at least {field.Min.Value}"
            });
        }

        if (field.Max.HasValue && value > field.Max.Value)
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' must be at most {field.Max.Value}"
            });
        }
    }

    private static void ValidateBooleanField(JsonElement element, string fieldPath, List<CharacterValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' must be a boolean"
            });
        }
    }

    private static void ValidateEnumField(JsonElement element, CharacterSchemaField field, string fieldPath, List<CharacterValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' must be a string"
            });
            return;
        }

        var value = element.GetString();
        if (field.Options is not null && field.Options.Count > 0 && value is not null)
        {
            if (!field.Options.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new CharacterValidationError
                {
                    FieldPath = fieldPath,
                    Message = $"Field '{fieldPath}' must be one of: {string.Join(", ", field.Options)}"
                });
            }
        }
    }

    private static void ValidateDiceExpressionField(JsonElement element, string fieldPath, List<CharacterValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' must be a string (dice expression)"
            });
        }
    }

    private static void ValidateListField(JsonElement element, string fieldPath, List<CharacterValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' must be an array"
            });
        }
    }

    private static void ValidateRepeatingField(JsonElement element, CharacterSchemaField field, string fieldPath, List<CharacterValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new CharacterValidationError
            {
                FieldPath = fieldPath,
                Message = $"Field '{fieldPath}' must be an array"
            });
            return;
        }

        // Validate each item against the item schema if defined
        if (field.ItemSchema is null || field.ItemSchema.Count == 0)
            return;

        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new CharacterValidationError
                {
                    FieldPath = $"{fieldPath}[{index}]",
                    Message = $"Item at '{fieldPath}[{index}]' must be an object"
                });
            }
            index++;
        }
    }

    private static void ValidateResourcePoolField(JsonElement element, CharacterSchemaField field, string fieldPath, List<CharacterValidationError> errors)
    {
        // Resource pool can be a number (current value) or an object with current/max
        if (element.ValueKind == JsonValueKind.Number)
        {
            // Simple numeric value is acceptable
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            // Object form: should have a "current" property
            if (!element.TryGetProperty("current", out var currentElement))
            {
                errors.Add(new CharacterValidationError
                {
                    FieldPath = fieldPath,
                    Message = $"Resource pool '{fieldPath}' must have a 'current' property"
                });
                return;
            }

            if (currentElement.ValueKind != JsonValueKind.Number)
            {
                errors.Add(new CharacterValidationError
                {
                    FieldPath = $"{fieldPath}.current",
                    Message = $"Field '{fieldPath}.current' must be a number"
                });
            }

            return;
        }

        errors.Add(new CharacterValidationError
        {
            FieldPath = fieldPath,
            Message = $"Field '{fieldPath}' must be a number or an object with 'current' property"
        });
    }

    /// <inheritdoc />
    public Dictionary<string, object?> ComputeCalculatedFields(string characterJson, CharacterSchema schema)
    {
        var result = new Dictionary<string, object?>();

        if (string.IsNullOrWhiteSpace(characterJson))
            return result;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(characterJson);
        }
        catch
        {
            return result;
        }

        // Extract all numeric field values from the JSON
        var fieldValues = ExtractNumericFieldValues(doc.RootElement);

        // Find all calculated fields and evaluate their formulas
        foreach (var section in schema.Sections)
        {
            foreach (var field in section.Fields)
            {
                if (field.Type == CharacterFieldType.Calculated && !string.IsNullOrWhiteSpace(field.Formula))
                {
                    var computed = FormulaEvaluator.Evaluate(field.Formula, fieldValues);
                    result[field.Id] = computed.HasValue ? (object)computed.Value : null;
                }
            }
        }

        doc.Dispose();
        return result;
    }

    private static Dictionary<string, double> ExtractNumericFieldValues(JsonElement root)
    {
        var values = new Dictionary<string, double>();

        if (root.ValueKind != JsonValueKind.Object)
            return values;

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var numValue))
            {
                values[property.Name] = numValue;
            }
        }

        return values;
    }

    /// <inheritdoc />
    public FormDescriptor GenerateFormDescriptor(CharacterSchema schema)
    {
        var sections = new List<FormSection>();

        foreach (var section in schema.Sections)
        {
            var fields = new List<FormField>();

            foreach (var field in section.Fields)
            {
                fields.Add(new FormField
                {
                    Id = field.Id,
                    Type = field.Type,
                    Label = field.Label,
                    Required = field.Required,
                    Min = field.Min,
                    Max = field.Max,
                    Options = field.Options,
                    Formula = field.Formula,
                    MaxField = field.MaxField,
                    IsReadOnly = field.Type == CharacterFieldType.Calculated
                });
            }

            sections.Add(new FormSection
            {
                Id = section.Id,
                Label = section.Label,
                Fields = fields,
                VisibleWhen = section.VisibleWhen
            });
        }

        return new FormDescriptor { Sections = sections };
    }

    /// <inheritdoc />
    public FieldMappingResult MapImportedFields(Dictionary<string, string> extractedFields, CharacterSchema schema)
    {
        var mapped = new Dictionary<string, string>();
        var unmapped = new Dictionary<string, string>();

        // Build a lookup of schema field IDs and labels for matching
        var schemaFieldLookup = BuildSchemaFieldLookup(schema);

        foreach (var (key, value) in extractedFields)
        {
            var matchedFieldId = FindBestMatch(key, schemaFieldLookup);

            if (matchedFieldId is not null)
            {
                // Avoid duplicate mappings - if already mapped, put in unmapped
                if (!mapped.ContainsKey(matchedFieldId))
                {
                    mapped[matchedFieldId] = value;
                }
                else
                {
                    unmapped[key] = value;
                }
            }
            else
            {
                unmapped[key] = value;
            }
        }

        return new FieldMappingResult
        {
            MappedFields = mapped,
            UnmappedFields = unmapped
        };
    }

    /// <summary>
    /// Builds a lookup dictionary mapping normalized names to schema field IDs.
    /// Each field can be matched by its ID or its label.
    /// </summary>
    private static Dictionary<string, string> BuildSchemaFieldLookup(CharacterSchema schema)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in schema.Sections)
        {
            foreach (var field in section.Fields)
            {
                // Map by field ID
                var normalizedId = NormalizeName(field.Id);
                lookup.TryAdd(normalizedId, field.Id);

                // Map by label if available
                if (!string.IsNullOrWhiteSpace(field.Label))
                {
                    var normalizedLabel = NormalizeName(field.Label);
                    lookup.TryAdd(normalizedLabel, field.Id);
                }
            }
        }

        return lookup;
    }

    /// <summary>
    /// Finds the best matching schema field ID for an imported field key.
    /// Uses exact match on normalized names first, then tries partial matching.
    /// </summary>
    private static string? FindBestMatch(string importedKey, Dictionary<string, string> schemaFieldLookup)
    {
        var normalizedKey = NormalizeName(importedKey);

        // Exact match on normalized name
        if (schemaFieldLookup.TryGetValue(normalizedKey, out var exactMatch))
            return exactMatch;

        // Try matching without common prefixes/suffixes
        foreach (var (schemaName, fieldId) in schemaFieldLookup)
        {
            if (normalizedKey.Contains(schemaName, StringComparison.OrdinalIgnoreCase) ||
                schemaName.Contains(normalizedKey, StringComparison.OrdinalIgnoreCase))
            {
                return fieldId;
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes a field name for comparison by lowercasing and removing
    /// common separators (spaces, underscores, hyphens).
    /// </summary>
    private static string NormalizeName(string name)
    {
        return name.ToLowerInvariant()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "");
    }
}
