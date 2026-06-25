using Microsoft.Extensions.Logging;
using Shinerock.Application.DTOs;
using Shinerock.Application.Interfaces;

namespace Shinerock.Application.Services;

/// <summary>
/// Shared logic for fetching show/episode data from Jellyfin.
/// Supports single and multi-show lookups.
/// </summary>
public class LibraryService
{
    private readonly IJellyfinService _jellyfinService;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(IJellyfinService jellyfinService, ILogger<LibraryService> logger)
    {
        _jellyfinService = jellyfinService;
        _logger = logger;
    }

    /// <summary>
    /// Fetch all episodes for one or more shows.
    /// Returns a combined list of episodes from all matched shows.
    /// </summary>
    public async Task<LibraryResult> GetEpisodesForShowsAsync(List<string> showNames, CancellationToken cancellationToken = default)
    {
        var allEpisodes = new List<EpisodeInfo>();
        var resolvedShows = new List<string>();
        var failedShows = new List<string>();

        foreach (var showName in showNames)
        {
            _logger.LogInformation("Searching Jellyfin for show: {Show}", showName);
            var (seriesId, seriesName) = await _jellyfinService.SearchSeriesAsync(showName, cancellationToken);

            if (seriesId is null)
            {
                _logger.LogWarning("Series not found: {Show}", showName);
                failedShows.Add(showName);
                continue;
            }

            _logger.LogInformation("Found series: {Name} (ID: {Id})", seriesName, seriesId);
            resolvedShows.Add(seriesName ?? showName);

            var episodes = await _jellyfinService.GetAllEpisodesAsync(seriesId, cancellationToken);
            _logger.LogInformation("Loaded {Count} episodes from {Show}", episodes.Count, seriesName);

            // Tag episodes with show name for context
            foreach (var ep in episodes)
            {
                ep.ShowName = seriesName ?? showName;
            }

            allEpisodes.AddRange(episodes);
        }

        return new LibraryResult
        {
            Episodes = allEpisodes,
            ResolvedShows = resolvedShows,
            FailedShows = failedShows
        };
    }
}

/// <summary>
/// Result of fetching episodes from one or more shows.
/// </summary>
public class LibraryResult
{
    public List<EpisodeInfo> Episodes { get; set; } = [];
    public List<string> ResolvedShows { get; set; } = [];
    public List<string> FailedShows { get; set; } = [];
}
