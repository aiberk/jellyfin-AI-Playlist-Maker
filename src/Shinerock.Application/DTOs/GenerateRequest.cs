namespace Shinerock.Application.DTOs;

/// <summary>
/// Request body for the generate endpoint.
/// Supports single show (via Show) or multiple shows (via Shows).
/// </summary>
public class GenerateRequest
{
    /// <summary>
    /// Single show name (e.g., "South Park"). For backwards compatibility.
    /// </summary>
    public string? Show { get; set; }

    /// <summary>
    /// Multiple show names for cross-show playlists (e.g., ["South Park", "Futurama"]).
    /// If provided, takes precedence over Show.
    /// </summary>
    public List<string>? Shows { get; set; }

    /// <summary>
    /// How many playlists to generate. Defaults to 5.
    /// </summary>
    public int PlaylistCount { get; set; } = 5;

    /// <summary>
    /// Optional instructions to guide the LLM (e.g., "focus on dark humor episodes", "only seasons 1-5").
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// If true, automatically create collections on Jellyfin after resolving IDs.
    /// Defaults to false (preview only).
    /// </summary>
    public bool Push { get; set; } = false;

    /// <summary>
    /// Get the resolved list of show names (handles both Show and Shows).
    /// </summary>
    public List<string> GetShowNames()
    {
        if (Shows is { Count: > 0 })
            return Shows;

        if (!string.IsNullOrWhiteSpace(Show))
            return [Show];

        return [];
    }
}
