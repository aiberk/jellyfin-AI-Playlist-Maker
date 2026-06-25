namespace Shinerock.Application.DTOs;

/// <summary>
/// Full response from the generate-and-create flow.
/// Includes what the LLM generated, resolution results, and a summary report.
/// </summary>
public class GenerateResponse
{
    public string Show { get; set; } = string.Empty;

    /// <summary>
    /// The raw playlists the LLM generated (before hitting Jellyfin).
    /// </summary>
    public List<PlaylistRequest> PlaylistsGenerated { get; set; } = [];

    /// <summary>
    /// The results of resolving IDs per playlist.
    /// </summary>
    public List<PlaylistResult> Results { get; set; } = [];

    /// <summary>
    /// Summary report with totals and all failed lookups.
    /// </summary>
    public ReportSummary Report { get; set; } = new();
}

/// <summary>
/// End-of-response summary showing success/failure stats.
/// </summary>
public class ReportSummary
{
    public int TotalPlaylists { get; set; }
    public int TotalTerms { get; set; }
    public int TotalResolved { get; set; }
    public int TotalFailed { get; set; }
    public List<FailedLookup> FailedLookups { get; set; } = [];
}

public class FailedLookup
{
    public string Playlist { get; set; } = string.Empty;
    public string Term { get; set; } = string.Empty;
}
