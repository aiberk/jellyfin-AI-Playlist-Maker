using Microsoft.AspNetCore.Mvc;
using Shinerock.Application.Interfaces;
using Shinerock.Application.Services;

namespace Shinerock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LibraryController : ControllerBase
{
    private readonly IJellyfinService _jellyfinService;
    private readonly LibraryService _libraryService;
    private readonly EpisodeResolverService _resolverService;

    public LibraryController(IJellyfinService jellyfinService, LibraryService libraryService, EpisodeResolverService resolverService)
    {
        _jellyfinService = jellyfinService;
        _libraryService = libraryService;
        _resolverService = resolverService;
    }

    /// <summary>
    /// Search for a show by name. Returns the series ID and name.
    /// </summary>
    [HttpGet("shows")]
    public async Task<IActionResult> SearchShow([FromQuery] string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Query parameter 'name' is required." });

        var (id, seriesName) = await _jellyfinService.SearchSeriesAsync(name, cancellationToken);

        if (id is null)
            return NotFound(new { error = $"Series '{name}' not found." });

        return Ok(new { id, name = seriesName });
    }

    /// <summary>
    /// Get all episodes for a show. Useful for manual playlist building.
    /// </summary>
    [HttpGet("shows/{showName}/episodes")]
    public async Task<IActionResult> GetEpisodes(string showName, CancellationToken cancellationToken)
    {
        var result = await _libraryService.GetEpisodesForShowsAsync([showName], cancellationToken);

        if (result.FailedShows.Count > 0)
            return NotFound(new { error = $"Series '{showName}' not found." });

        return Ok(new
        {
            show = result.ResolvedShows.FirstOrDefault(),
            totalEpisodes = result.Episodes.Count,
            episodes = result.Episodes.Select(e => new
            {
                e.Id,
                e.Name,
                season = e.SeasonNumber,
                episode = e.EpisodeNumber
            })
        });
    }

    /// <summary>
    /// Resolve episode search terms to Jellyfin IDs.
    /// Pass an array of episode names and get back their IDs.
    /// </summary>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /api/library/resolve
    ///     { "show": "South Park", "terms": ["Raisins", "Casa Bonita", "Scott Tenorman Must Die"] }
    ///
    /// </remarks>
    [HttpPost("resolve")]
    public async Task<IActionResult> ResolveIds([FromBody] ResolveRequest request, CancellationToken cancellationToken)
    {
        if (request.Terms is null || request.Terms.Count == 0)
            return BadRequest(new { error = "Field 'terms' is required (array of episode names)." });

        List<EpisodeInfo>? knownEpisodes = null;

        // If a show is provided, fetch its episodes for better matching
        if (!string.IsNullOrWhiteSpace(request.Show))
        {
            var libraryResult = await _libraryService.GetEpisodesForShowsAsync([request.Show], cancellationToken);
            if (libraryResult.Episodes.Count > 0)
                knownEpisodes = libraryResult.Episodes;
        }

        var playlists = new List<Application.DTOs.PlaylistRequest>
        {
            new() { Title = "resolve", Terms = request.Terms }
        };

        var result = await _resolverService.ResolvePlaylistsAsync(playlists, knownEpisodes, cancellationToken);
        var playlistResult = result.Results[0];

        return Ok(new
        {
            resolved = playlistResult.Ids.Count,
            failed = playlistResult.FailedTerms.Count,
            ids = playlistResult.Ids,
            failedTerms = playlistResult.FailedTerms
        });
    }

    /// <summary>
    /// List all collections on the server.
    /// </summary>
    [HttpGet("collections")]
    public async Task<IActionResult> GetCollections(CancellationToken cancellationToken)
    {
        var collections = await _jellyfinService.GetAllCollectionsAsync(cancellationToken);
        return Ok(collections);
    }

    /// <summary>
    /// Get items inside a specific collection.
    /// </summary>
    [HttpGet("collections/{collectionId}/items")]
    public async Task<IActionResult> GetCollectionItems(string collectionId, CancellationToken cancellationToken)
    {
        var items = await _jellyfinService.GetCollectionItemsAsync(collectionId, cancellationToken);

        if (items.Count == 0)
            return NotFound(new { error = "Collection not found or empty." });

        return Ok(new
        {
            collectionId,
            itemCount = items.Count,
            items = items.Select(e => new
            {
                e.Id,
                e.Name,
                season = e.SeasonNumber,
                episode = e.EpisodeNumber
            })
        });
    }
}

public class ResolveRequest
{
    /// <summary>
    /// Optional: show name for better matching (fetches episode catalogue first).
    /// </summary>
    public string? Show { get; set; }

    /// <summary>
    /// Episode names to resolve to Jellyfin IDs.
    /// </summary>
    public List<string> Terms { get; set; } = [];
}
