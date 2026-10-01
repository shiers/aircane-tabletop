using Aircane.Application.GameSystems;
using Aircane.Application.Validation;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Infrastructure.Persistence;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Infrastructure.GameSystems;

/// <summary>
/// EF Core-backed implementation of <see cref="ISystemRegistry"/>.
/// Manages Game System Definition CRUD, import/export, validation, and versioning.
/// </summary>
public class SystemRegistry : ISystemRegistry
{
    private readonly AircaneDbContext _db;
    private readonly IGameSystemDefinitionSerializer _serializer;
    private readonly IValidator<GameSystemDefinition> _validator;

    public SystemRegistry(
        AircaneDbContext db,
        IGameSystemDefinitionSerializer serializer,
        IValidator<GameSystemDefinition> validator)
    {
        _db = db;
        _serializer = serializer;
        _validator = validator;
    }

    /// <inheritdoc />
    public async Task<GameSystemDefinition> GetByIdAsync(Guid definitionId, CancellationToken ct = default)
    {
        var entity = await _db.GameSystemDefinitions
            .FirstOrDefaultAsync(d => d.Id == definitionId, ct)
            ?? throw new KeyNotFoundException($"Game System Definition with ID '{definitionId}' not found.");

        HydrateFromJson(entity);
        return entity;
    }

    /// <inheritdoc />
    public async Task<GameSystemDefinition> GetByCampaignAsync(Guid campaignId, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw new KeyNotFoundException($"Campaign with ID '{campaignId}' not found.");

        if (campaign.GameSystemDefinitionId is null)
            throw new InvalidOperationException(
                $"Campaign '{campaignId}' does not have a bound Game System Definition.");

        return await GetByIdAsync(campaign.GameSystemDefinitionId.Value, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GameSystemDefinitionSummary>> ListAsync(CancellationToken ct = default)
    {
        return await _db.GameSystemDefinitions
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new GameSystemDefinitionSummary
            {
                Id = d.Id,
                Identifier = d.Identifier,
                Name = d.Name,
                Version = d.Version,
                Genre = d.Genre,
                License = d.License,
                IsBuiltIn = d.IsBuiltIn,
                IsActive = d.IsActive
            })
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<GameSystemDefinition> ImportAsync(Stream definitionFile, string format, CancellationToken ct = default)
    {
        var parseResult = _serializer.ParseJson(definitionFile);

        if (!parseResult.IsSuccess)
        {
            var errorMessages = string.Join("; ", parseResult.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Failed to parse definition: {errorMessages}");
        }

        var definition = parseResult.Definition!;

        var validationResult = await _validator.ValidateAsync(definition, ct);
        if (!validationResult.IsValid)
        {
            var errorMessages = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            throw new ValidationException($"Definition validation failed: {errorMessages}", validationResult.Errors);
        }

        // Serialize back to JSON for storage
        definition.DefinitionJson = _serializer.PrintJson(definition);
        definition.IsActive = true;
        definition.UpdatedAt = DateTimeOffset.UtcNow;

        _db.GameSystemDefinitions.Add(definition);

        // Create initial version
        var version = new GameSystemDefinitionVersion(
            definition.Id,
            definition.Version,
            definition.DefinitionJson);
        _db.GameSystemDefinitionVersions.Add(version);

        await _db.SaveChangesAsync(ct);

        return definition;
    }

    /// <inheritdoc />
    public async Task<Stream> ExportAsync(Guid definitionId, string format, CancellationToken ct = default)
    {
        var entity = await _db.GameSystemDefinitions
            .FirstOrDefaultAsync(d => d.Id == definitionId, ct)
            ?? throw new KeyNotFoundException($"Game System Definition with ID '{definitionId}' not found.");

        // Hydrate to get the rich domain model, then serialize to canonical JSON
        HydrateFromJson(entity);
        var json = _serializer.PrintJson(entity);

        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
        await writer.FlushAsync(ct);
        stream.Position = 0;

        return stream;
    }

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateAsync(Stream definitionFile, string format, CancellationToken ct = default)
    {
        var parseResult = _serializer.ParseJson(definitionFile);

        if (!parseResult.IsSuccess)
        {
            var failures = parseResult.Errors.Select(e =>
                new ValidationFailure(e.FieldPath ?? "document", e.Message)).ToList();
            return new ValidationResult(failures);
        }

        var definition = parseResult.Definition!;
        return await _validator.ValidateAsync(definition, ct);
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(GameSystemDefinition definition, CancellationToken ct = default)
    {
        var validationResult = await _validator.ValidateAsync(definition, ct);
        if (!validationResult.IsValid)
        {
            var errorMessages = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            throw new ValidationException($"Definition validation failed: {errorMessages}", validationResult.Errors);
        }

        // Serialize the definition to JSON for storage
        definition.DefinitionJson = _serializer.PrintJson(definition);
        definition.IsActive = true;
        definition.UpdatedAt = DateTimeOffset.UtcNow;

        _db.GameSystemDefinitions.Add(definition);

        // Create initial version record
        var version = new GameSystemDefinitionVersion(
            definition.Id,
            definition.Version,
            definition.DefinitionJson);
        _db.GameSystemDefinitionVersions.Add(version);

        await _db.SaveChangesAsync(ct);

        return definition.Id;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Guid definitionId, GameSystemDefinition definition, CancellationToken ct = default)
    {
        var existing = await _db.GameSystemDefinitions
            .FirstOrDefaultAsync(d => d.Id == definitionId, ct)
            ?? throw new KeyNotFoundException($"Game System Definition with ID '{definitionId}' not found.");

        var validationResult = await _validator.ValidateAsync(definition, ct);
        if (!validationResult.IsValid)
        {
            var errorMessages = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            throw new ValidationException($"Definition validation failed: {errorMessages}", validationResult.Errors);
        }

        // Update entity fields
        existing.Identifier = definition.Identifier;
        existing.Name = definition.Name;
        existing.Version = definition.Version;
        existing.SchemaVersion = definition.SchemaVersion;
        existing.Publisher = definition.Publisher;
        existing.Genre = definition.Genre;
        existing.Description = definition.Description;
        existing.License = definition.License;
        existing.DefinitionJson = _serializer.PrintJson(definition);
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        // Create a new version snapshot - campaigns stay pinned to their version
        var version = new GameSystemDefinitionVersion(
            definitionId,
            definition.Version,
            existing.DefinitionJson);
        _db.GameSystemDefinitionVersions.Add(version);

        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(Guid definitionId, CancellationToken ct = default)
    {
        var existing = await _db.GameSystemDefinitions
            .FirstOrDefaultAsync(d => d.Id == definitionId, ct)
            ?? throw new KeyNotFoundException($"Game System Definition with ID '{definitionId}' not found.");

        // Check if any active campaigns reference this definition
        var campaignCount = await _db.Campaigns
            .CountAsync(c => c.GameSystemDefinitionId == definitionId, ct);

        if (campaignCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot deactivate definition '{existing.Name}': {campaignCount} campaign(s) still reference it.");
        }

        existing.IsActive = false;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Deserializes the DefinitionJson blob back into the rich domain properties
    /// (DiceConventions, ResolutionRules, CharacterSchema, etc.).
    /// </summary>
    private void HydrateFromJson(GameSystemDefinition entity)
    {
        if (string.IsNullOrWhiteSpace(entity.DefinitionJson) || entity.DefinitionJson == "{}")
            return;

        var parseResult = _serializer.ParseJson(entity.DefinitionJson);
        if (!parseResult.IsSuccess || parseResult.Definition is null)
            return;

        var parsed = parseResult.Definition;

        // Hydrate the rich domain properties from the parsed JSON
        entity.DiceConventions = parsed.DiceConventions;
        entity.ResolutionRules = parsed.ResolutionRules;
        entity.CharacterSchema = parsed.CharacterSchema;
        entity.ConditionSet = parsed.ConditionSet;
        entity.ActionEconomy = parsed.ActionEconomy;
        entity.EncounterBudget = parsed.EncounterBudget;
        entity.AiGuidance = parsed.AiGuidance;
        entity.Tags = parsed.Tags;
    }
}
