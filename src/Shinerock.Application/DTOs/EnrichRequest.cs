namespace Shinerock.Application.DTOs;

/// <summary>
/// Request for the AI enrich endpoint.
/// </summary>
public class EnrichRequest
{
    /// <summary>
    /// If true, overwrite existing descriptions. If false (default), only fill in empty ones.
    /// </summary>
    public bool Overwrite { get; set; } = false;

    /// <summary>
    /// Optional: only enrich specific collection IDs. If empty, processes all collections.
    /// </summary>
    public List<string>? CollectionIds { get; set; }
}

/// <summary>
/// Manual metadata update for a single collection.
/// </summary>
public class MetadataUpdateItem
{
    public string CollectionId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Tagline { get; set; }
}

/// <summary>
/// Result of enriching a single collection.
/// </summary>
public class EnrichResult
{
    public string CollectionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Tagline { get; set; }
    public bool Updated { get; set; }
    public string? Error { get; set; }
}
