using Microsoft.Extensions.Logging;
using Shinerock.Application.DTOs;
using Shinerock.Application.Interfaces;

namespace Shinerock.Application.Services;

/// <summary>
/// Shared logic for resolving episode search terms to Jellyfin IDs.
/// Used by both the manual collections endpoint and the AI generate endpoint.
/// </summary>
public class EpisodeResolverService
{
    private readonly IJellyfinService _jellyfinService;
    private readonly ILogger<EpisodeResolverService> _logger;

    public EpisodeResolverService(IJellyfinService jellyfinService, ILogger<EpisodeResolverService> logger)
    {
        _jellyfinService = jellyfinService;
        _logger = logger;
    }

    /// <summary>
    /// Resolve a list of playlists against a known episode catalogue.
    /// Uses exact match → containment match → Jellyfin fuzzy search as fallback.
    /// </summary>
    public async Task<ResolveResult> ResolvePlaylistsAsync(
        List<PlaylistRequest> playlists,
        List<EpisodeInfo>? knownEpisodes = null,
        CancellationToken cancellationToken = default)
    {
        // Build lookup if we have known episodes
        var episodeLookup = new Dictionary<string, EpisodeInfo>(StringComparer.OrdinalIgnoreCase);
        if (knownEpisodes is not null)
        {
            foreach (var ep in knownEpisodes)
                episodeLookup.TryAdd(ep.Name, ep);
        }

        var results = new List<PlaylistResult>();
        var allFailedLookups = new List<FailedLookup>();
        var totalTerms = 0;
        var totalResolved = 0;

        foreach (var playlist in playlists)
        {
            _logger.LogInformation("Resolving playlist: {Title} ({Count} terms)", playlist.Title, playlist.Terms.Count);
            var result = new PlaylistResult
            {
                Title = playlist.Title,
                Description = playlist.Description,
                Tagline = playlist.Tagline
            };

            foreach (var term in playlist.Terms)
            {
                totalTerms++;
                string? resolvedId = null;

                // Strategy 1: Match against known episode list (if available)
                if (knownEpisodes is not null)
                {
                    var matched = TryResolveFromCatalogue(term, episodeLookup, knownEpisodes);
                    if (matched is not null)
                    {
                        _logger.LogInformation("  Matched: {Term} -> {Name} ({Id})", term, matched.Name, matched.Id);
                        resolvedId = matched.Id;
                    }
                }

                // Strategy 2: Fallback to Jellyfin fuzzy search
                if (resolvedId is null)
                {
                    var (id, name) = await _jellyfinService.SearchEpisodeAsync(term, cancellationToken);
                    if (id is not null)
                    {
                        _logger.LogInformation("  Fuzzy match: {Term} -> {Name} ({Id})", term, name, id);
                        resolvedId = id;
                    }
                }

                // Record result
                if (resolvedId is not null)
                {
                    result.Ids.Add(resolvedId);
                    totalResolved++;
                }
                else
                {
                    _logger.LogWarning("  No match for term: {Term}", term);
                    result.FailedTerms.Add(term);
                    allFailedLookups.Add(new FailedLookup { Playlist = playlist.Title, Term = term });
                }
            }

            result.Success = result.Ids.Count > 0;
            if (!result.Success)
                result.Error = "No episode IDs found for any search terms.";
            else if (result.FailedTerms.Count > 0)
                result.Error = $"{result.FailedTerms.Count} term(s) could not be resolved.";

            results.Add(result);
        }

        return new ResolveResult
        {
            Results = results,
            Report = new ReportSummary
            {
                TotalPlaylists = playlists.Count,
                TotalTerms = totalTerms,
                TotalResolved = totalResolved,
                TotalFailed = allFailedLookups.Count,
                FailedLookups = allFailedLookups
            }
        };
    }

    /// <summary>
    /// Try to match a term against the known episode catalogue.
    /// Exact match first, then containment.
    /// </summary>
    private static EpisodeInfo? TryResolveFromCatalogue(string term, Dictionary<string, EpisodeInfo> lookup, List<EpisodeInfo> allEpisodes)
    {
        if (lookup.TryGetValue(term, out var exact))
            return exact;

        return allEpisodes.FirstOrDefault(e =>
            e.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            term.Contains(e.Name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Result of resolving playlists — used internally between services.
/// </summary>
public class ResolveResult
{
    public List<PlaylistResult> Results { get; set; } = [];
    public ReportSummary Report { get; set; } = new();
}
