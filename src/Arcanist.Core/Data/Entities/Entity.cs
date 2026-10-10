namespace Arcanist.Core.Data.Entities;

/// <summary>
/// Base for entities with a surrogate key. Keys are UUIDv7 generated in the application, so rows can be
/// copied between database providers (for example SQLite to PostgreSQL) without remapping identifiers.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}
