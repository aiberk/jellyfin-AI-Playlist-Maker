namespace Shinerock.Application.DTOs;

/// <summary>
/// Incoming request: an array of these makes up the full request body.
/// </summary>
public class PlaylistRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Tagline { get; set; }
    public List<string> Terms { get; set; } = [];
}
