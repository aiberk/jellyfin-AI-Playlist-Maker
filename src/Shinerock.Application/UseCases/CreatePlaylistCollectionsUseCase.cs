using Microsoft.Extensions.Logging;
using Shinerock.Application.DTOs;
using Shinerock.Application.Interfaces;
using Shinerock.Application.Services;

namespace Shinerock.Application.UseCases;

/// <summary>
/// Resolves search terms → gets IDs → creates collections on Jellyfin.
/// Uses the shared EpisodeResolverService for ID resolution.
/// </summary>
public class CreatePlaylistCollectionsUseCase
{
    private readonly IJellyfinService _jellyfinService;
    private readonly EpisodeResolverService _resolverService;
    private readonly ILogger<CreatePlaylistCollectionsUseCase> _logger;

    public CreatePlaylistCollectionsUseCase(
        IJellyfinService jellyfinService,
        EpisodeResolverService resolverService,
        ILogger<CreatePlaylistCollectionsUseCase> logger)
    {
        _jellyfinService = jellyfinService;
        _resolverService = resolverService;
        _logger = logger;
    }

    public async Task<List<PlaylistResult>> ExecuteAsync(List<PlaylistRequest> playlists, CancellationToken cancellationToken = default)
    {
        // Step 1: Resolve all terms to IDs using shared resolver (no known catalogue — uses fuzzy search)
        var resolveResult = await _resolverService.ResolvePlaylistsAsync(playlists, knownEpisodes: null, cancellationToken);

        // Step 2: Create collections on Jellyfin for successful playlists
        foreach (var result in resolveResult.Results)
        {
            if (result.Ids.Count > 0)
            {
                try
                {
                    var collectionId = await _jellyfinService.CreateCollectionAsync(result.Title, result.Ids, cancellationToken);
                    result.CollectionId = collectionId;
                    result.Success = true;
                    _logger.LogInformation("Created collection: {Title} (ID: {CollectionId})", result.Title, collectionId);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Error = ex.Message;
                    _logger.LogError(ex, "Failed to create collection: {Title}", result.Title);
                }
            }
        }

        return resolveResult.Results;
    }
}
