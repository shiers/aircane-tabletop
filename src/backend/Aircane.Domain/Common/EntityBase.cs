namespace Aircane.Domain.Common;

/// <summary>
/// Base class for all domain entities. Provides a stable identity and creation timestamp.
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    protected EntityBase()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
    }

    protected EntityBase(Guid id, DateTimeOffset createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
    }
}
