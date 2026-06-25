using Microsoft.AspNetCore.Mvc;
using Shinerock.Application.DTOs;
using Shinerock.Application.Interfaces;
using Shinerock.Application.Services;
using Shinerock.Application.UseCases;

namespace Shinerock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CollectionsController : ControllerBase
{
    private readonly CreatePlaylistCollectionsUseCase _createPlaylistCollections;
    private readonly IJellyfinService _jellyfinService;
    private readonly EpisodeResolverService _resolverService;
    private readonly LibraryService _libraryService;
    private readonly ILogger<CollectionsController> _logger;

    public CollectionsController(
        CreatePlaylistCollectionsUseCase createPlaylistCollections,
        IJellyfinService jellyfinService,
        EpisodeResolverService resolverService,
        LibraryService libraryService,
        ILogger<CollectionsController> logger)
    {
        _createPlaylistCollections = createPlaylistCollections;
        _jellyfinService = jellyfinService;
        _resolverService = resolverService;
        _libraryService = libraryService;
        _logger = logger;
    }

    /// <summary>
    /// Create new collections from playlist data. Resolves episode IDs and pushes to Jellyfin.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(List<PlaylistResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCollections([FromBody] List<PlaylistRequest> playlists, CancellationToken cancellationToken)
    {
        if (playlists is null || playlists.Count == 0)
            return BadRequest(new { error = "Request body must be a non-empty array of playlist objects." });

        for (int i = 0; i < playlists.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(playlists[i].Title))
                return BadRequest(new { error = $"Playlist at index {i} is missing a title." });
            if (playlists[i].Terms is null || playlists[i].Terms.Count == 0)
                return BadRequest(new { error = $"Playlist '{playlists[i].Title}' has no search terms." });
        }

        _logger.LogInformation("Creating {Count} collection(s)", playlists.Count);
        var results = await _createPlaylistCollections.ExecuteAsync(playlists, cancellationToken);
        return Ok(results);
    }

    /// <summary>
    /// Add episodes to an existing collection. Accepts collection by ID or name.
    /// </summary>
    /// <remarks>
    /// Sample requests:
    ///
    ///     POST /api/collections/add
    ///     { "collection": "South Park: Butters", "terms": ["Marjorine", "Butters' Very Own Episode"], "show": "South Park" }
    ///
    ///     POST /api/collections/add
    ///     { "collectionId": "abc123", "ids": ["episode-id-1", "episode-id-2"] }
    ///
    /// </remarks>
    [HttpPost("add")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddToCollection([FromBody] AddToCollectionRequest request, CancellationToken cancellationToken)
    {
        // Resolve the collection ID
        var collectionId = await ResolveCollectionIdAsync(request.CollectionId, request.Collection, cancellationToken);
        if (collectionId is null)
            return NotFound(new { error = $"Collection not found. Provide a valid 'collectionId' or 'collection' name." });

        // Get the episode IDs to add
        var idsToAdd = new List<string>();

        // Direct IDs provided
        if (request.Ids is { Count: > 0 })
        {
            idsToAdd.AddRange(request.Ids);
        }

        // Terms to resolve
        if (request.Terms is { Count: > 0 })
        {
            List<EpisodeInfo>? knownEpisodes = null;
            if (!string.IsNullOrWhiteSpace(request.Show))
            {
                var libraryResult = await _libraryService.GetEpisodesForShowsAsync([request.Show], cancellationToken);
                if (libraryResult.Episodes.Count > 0)
                    knownEpisodes = libraryResult.Episodes;
            }

            var playlists = new List<PlaylistRequest> { new() { Title = "add", Terms = request.Terms } };
            var resolved = await _resolverService.ResolvePlaylistsAsync(playlists, knownEpisodes, cancellationToken);
            idsToAdd.AddRange(resolved.Results[0].Ids);

            if (resolved.Results[0].FailedTerms.Count > 0)
            {
                // Still add what we can, but report failures
                await _jellyfinService.AddToCollectionAsync(collectionId, idsToAdd, cancellationToken);
                return Ok(new
                {
                    added = idsToAdd.Count,
                    failedTerms = resolved.Results[0].FailedTerms,
                    message = $"Added {idsToAdd.Count} items. {resolved.Results[0].FailedTerms.Count} term(s) could not be resolved."
                });
            }
        }

        if (idsToAdd.Count == 0)
            return BadRequest(new { error = "Provide 'ids' (direct episode IDs) or 'terms' (episode names to resolve)." });

        await _jellyfinService.AddToCollectionAsync(collectionId, idsToAdd, cancellationToken);

        return Ok(new { added = idsToAdd.Count, collectionId });
    }

    /// <summary>
    /// Remove episodes from an existing collection.
    /// </summary>
    [HttpPost("remove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFromCollection([FromBody] RemoveFromCollectionRequest request, CancellationToken cancellationToken)
    {
        var collectionId = await ResolveCollectionIdAsync(request.CollectionId, request.Collection, cancellationToken);
        if (collectionId is null)
            return NotFound(new { error = "Collection not found." });

        if (request.Ids is null || request.Ids.Count == 0)
            return BadRequest(new { error = "Provide 'ids' array of episode IDs to remove." });

        await _jellyfinService.RemoveFromCollectionAsync(collectionId, request.Ids, cancellationToken);
        return Ok(new { removed = request.Ids.Count, collectionId });
    }

    /// <summary>
    /// Delete a collection entirely. Accepts ID or name.
    /// </summary>
    /// <remarks>
    /// Sample requests:
    ///
    ///     DELETE /api/collections?id=abc123
    ///     DELETE /api/collections?name=South Park: Butters
    ///
    /// </remarks>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCollection([FromQuery] string? id, [FromQuery] string? name, CancellationToken cancellationToken)
    {
        var collectionId = await ResolveCollectionIdAsync(id, name, cancellationToken);
        if (collectionId is null)
            return NotFound(new { error = "Collection not found. Provide 'id' or 'name' query parameter." });

        // Get name for confirmation message
        var collections = await _jellyfinService.GetAllCollectionsAsync(cancellationToken);
        var collectionName = collections.FirstOrDefault(c => c.Id == collectionId)?.Name ?? collectionId;

        await _jellyfinService.DeleteCollectionAsync(collectionId, cancellationToken);
        return Ok(new { deleted = true, collectionId, name = collectionName });
    }

    /// <summary>
    /// Delete multiple collections at once.
    /// </summary>
    [HttpPost("delete-batch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteBatch([FromBody] DeleteBatchRequest request, CancellationToken cancellationToken)
    {
        if (request.Ids is null || request.Ids.Count == 0)
            return BadRequest(new { error = "Provide 'ids' array of collection IDs to delete." });

        var results = new List<object>();
        foreach (var collId in request.Ids)
        {
            try
            {
                await _jellyfinService.DeleteCollectionAsync(collId, cancellationToken);
                results.Add(new { id = collId, deleted = true });
            }
            catch (Exception ex)
            {
                results.Add(new { id = collId, deleted = false, error = ex.Message });
            }
        }

        return Ok(new { deleted = results.Count(r => ((dynamic)r).deleted), results });
    }

    /// <summary>
    /// Resolve a collection by ID or name.
    /// </summary>
    private async Task<string?> ResolveCollectionIdAsync(string? id, string? name, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(id))
            return id;

        if (!string.IsNullOrWhiteSpace(name))
        {
            var collections = await _jellyfinService.GetAllCollectionsAsync(cancellationToken);
            var match = collections.FirstOrDefault(c =>
                c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            return match?.Id;
        }

        return null;
    }
}

public class AddToCollectionRequest
{
    /// <summary>Direct collection ID.</summary>
    public string? CollectionId { get; set; }

    /// <summary>Collection name (will be looked up).</summary>
    public string? Collection { get; set; }

    /// <summary>Optional: show name for better term resolution.</summary>
    public string? Show { get; set; }

    /// <summary>Direct episode IDs to add.</summary>
    public List<string>? Ids { get; set; }

    /// <summary>Episode names to resolve and add.</summary>
    public List<string>? Terms { get; set; }
}

public class RemoveFromCollectionRequest
{
    public string? CollectionId { get; set; }
    public string? Collection { get; set; }
    public List<string>? Ids { get; set; }
}

public class DeleteBatchRequest
{
    public List<string> Ids { get; set; } = [];
}
