namespace Shinerock.Application.DTOs;

/// <summary>
/// Result after resolving IDs (and optionally creating a collection).
/// </summary>
public class PlaylistResult
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Tagline { get; set; }
    public List<string> Ids { get; set; } = [];
    public List<string> FailedTerms { get; set; } = [];
    public string? CollectionId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
