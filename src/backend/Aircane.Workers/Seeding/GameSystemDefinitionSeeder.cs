using Aircane.Application.GameSystems;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Infrastructure.GameSystems.Seeds;
using Aircane.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aircane.Workers.Seeding;

/// <summary>
/// Seeds the built-in Game System Definitions (mechanics definitions) into the database on
/// startup: D&amp;D 5e 2014, Generic Freeform, and Pathfinder 2e Remaster. Idempotent — an existing
/// definition with the same well-known ID is left untouched.
/// </summary>
/// <remarks>
/// These are mechanic DEFINITIONS (dice conventions, resolution rules, conditions, action economy),
/// distinct from the open-content rules TEXT bundles handled by <see cref="BuiltInContentSeeder"/>,
/// and distinct from user-created definitions managed via the SystemRegistry API.
/// </remarks>
public sealed class GameSystemDefinitionSeeder
{
    private readonly AircaneDbContext _db;
    private readonly IGameSystemDefinitionSerializer _serializer;
    private readonly IValidator<GameSystemDefinition> _validator;
    private readonly ILogger<GameSystemDefinitionSeeder> _logger;

    public GameSystemDefinitionSeeder(
        AircaneDbContext db,
        IGameSystemDefinitionSerializer serializer,
        IValidator<GameSystemDefinition> validator,
        ILogger<GameSystemDefinitionSeeder> logger)
    {
        _db = db;
        _serializer = serializer;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>Seeds all built-in definitions. Safe to call on every startup.</summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var seeds = new[]
        {
            DnD5e2014Seed.Create(),
            GenericFreeformSeed.Create(),
            Pathfinder2eRemasterSeed.Create(),
        };

        foreach (var definition in seeds)
        {
            try
            {
                await SeedOneAsync(definition, ct);
            }
            catch (Exception ex)
            {
                // A single failing definition must not block the others or app startup.
                _logger.LogError(ex,
                    "GameSystemDefinitionSeeder: failed to seed definition '{Identifier}'.",
                    definition.Identifier);
            }
        }

        try
        {
            await SeedAliasesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GameSystemDefinitionSeeder: failed to seed game-system aliases.");
        }
    }

    /// <summary>
    /// Seeds common short-hand aliases for the built-in definitions (idempotent). Only aliases
    /// that don't already exist for a definition are added, so hosts can add custom aliases
    /// without them being removed on the next startup.
    /// </summary>
    private async Task SeedAliasesAsync(CancellationToken ct)
    {
        var aliasesByDefinition = new Dictionary<Guid, string[]>
        {
            [DnD5e2014Seed.DefinitionId] =
                ["D&D 5e", "DnD 5e", "5e", "D&D5e", "DnD5", "D&D 5th Edition"],
            [Pathfinder2eRemasterSeed.DefinitionId] =
                ["Pathfinder 2e", "PF2e", "Pf2", "Pathfinder Second Edition"],
        };

        foreach (var (definitionId, aliases) in aliasesByDefinition)
        {
            var definitionExists = await _db.GameSystemDefinitions.AnyAsync(d => d.Id == definitionId, ct);
            if (!definitionExists)
                continue;

            var existing = await _db.GameSystemAliases
                .Where(a => a.GameSystemDefinitionId == definitionId)
                .Select(a => a.Alias)
                .ToListAsync(ct);
            var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

            var added = 0;
            foreach (var alias in aliases)
            {
                if (existingSet.Contains(alias))
                    continue;
                _db.GameSystemAliases.Add(new GameSystemAlias(definitionId, alias));
                added++;
            }

            if (added > 0)
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "GameSystemDefinitionSeeder: seeded {Count} alias(es) for definition {DefinitionId}.",
                    added, definitionId);
            }
        }
    }

    private async Task SeedOneAsync(GameSystemDefinition definition, CancellationToken ct)
    {
        var exists = await _db.GameSystemDefinitions.AnyAsync(d => d.Id == definition.Id, ct);
        if (exists)
        {
            _logger.LogInformation(
                "GameSystemDefinitionSeeder: definition '{Identifier}' already present; skipping.",
                definition.Identifier);
            return;
        }

        // Guard: a built-in seed must be valid (mirrors SystemRegistry.CreateAsync). A broken seed
        // is a build-time bug, so surface it rather than persisting invalid data.
        var validation = await _validator.ValidateAsync(definition, ct);
        if (!validation.IsValid)
        {
            _logger.LogError(
                "GameSystemDefinitionSeeder: built-in definition '{Identifier}' is invalid; skipping. Errors: {Errors}",
                definition.Identifier,
                string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
            return;
        }

        definition.DefinitionJson = _serializer.PrintJson(definition);
        definition.IsActive = true;
        definition.UpdatedAt = DateTimeOffset.UtcNow;

        _db.GameSystemDefinitions.Add(definition);
        _db.GameSystemDefinitionVersions.Add(
            new GameSystemDefinitionVersion(definition.Id, definition.Version, definition.DefinitionJson));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "GameSystemDefinitionSeeder: seeded built-in definition '{Identifier}' ({Name}).",
            definition.Identifier, definition.Name);
    }
}
