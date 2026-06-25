namespace Shinerock.Domain.Entities;

/// <summary>
/// Base class for all domain entities. Provides a common Id property.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
