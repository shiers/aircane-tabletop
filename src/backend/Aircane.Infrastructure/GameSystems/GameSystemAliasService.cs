using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Infrastructure.GameSystems;

/// <summary>
/// EF Core implementation of <see cref="IGameSystemAliasService"/>.
/// </summary>
public sealed class GameSystemAliasService : IGameSystemAliasService
{
    private readonly AircaneDbContext _db;

    public GameSystemAliasService(AircaneDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GameSystemAliasDto>> ListAsync(Guid definitionId, CancellationToken ct = default)
    {
        return await _db.GameSystemAliases
            .AsNoTracking()
            .Where(a => a.GameSystemDefinitionId == definitionId)
            .OrderBy(a => a.Alias)
            .Select(a => new GameSystemAliasDto(a.Id, a.GameSystemDefinitionId, a.Alias, a.CreatedAt))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<GameSystemAliasDto> AddAsync(Guid definitionId, string alias, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias must not be empty.", nameof(alias));

        var trimmed = alias.Trim();

        var definitionExists = await _db.GameSystemDefinitions.AnyAsync(d => d.Id == definitionId, ct);
        if (!definitionExists)
            throw new KeyNotFoundException($"Game system definition {definitionId} not found.");

        var existing = await _db.GameSystemAliases
            .FirstOrDefaultAsync(
                a => a.GameSystemDefinitionId == definitionId && a.Alias.ToLower() == trimmed.ToLower(), ct);
        if (existing is not null)
            return new GameSystemAliasDto(existing.Id, existing.GameSystemDefinitionId, existing.Alias, existing.CreatedAt);

        var entity = new GameSystemAlias(definitionId, trimmed);
        _db.GameSystemAliases.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new GameSystemAliasDto(entity.Id, entity.GameSystemDefinitionId, entity.Alias, entity.CreatedAt);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid aliasId, CancellationToken ct = default)
    {
        var entity = await _db.GameSystemAliases.FirstOrDefaultAsync(a => a.Id == aliasId, ct);
        if (entity is null)
            return;

        _db.GameSystemAliases.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
