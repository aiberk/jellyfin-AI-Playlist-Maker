using Microsoft.Extensions.Logging;
using Shinerock.Application.DTOs;
using Shinerock.Application.Interfaces;
using Shinerock.Application.Services;

namespace Shinerock.Application.UseCases;

/// <summary>
/// Orchestrates: Fetch show data → LLM generates playlists → resolve IDs → report.
/// Supports single and multi-show playlists.
/// </summary>
public class GenerateAndCreateCollectionsUseCase
{
    private readonly ILlmService _llmService;
    private readonly IJellyfinService _jellyfinService;
    private readonly LibraryService _libraryService;
    private readonly EpisodeResolverService _resolverService;
    private readonly ILogger<GenerateAndCreateCollectionsUseCase> _logger;

    public GenerateAndCreateCollectionsUseCase(
        ILlmService llmService,
        IJellyfinService jellyfinService,
        LibraryService libraryService,
        EpisodeResolverService resolverService,
        ILogger<GenerateAndCreateCollectionsUseCase> logger)
    {
        _llmService = llmService;
        _jellyfinService = jellyfinService;
        _libraryService = libraryService;
        _resolverService = resolverService;
        _logger = logger;
    }

    public async Task<GenerateResponse> ExecuteAsync(GenerateRequest request, CancellationToken cancellationToken = default)
    {
        var showNames = request.GetShowNames();
        var showLabel = string.Join(", ", showNames);

        // Step 1: Fetch episodes from all requested shows
        _logger.LogInformation("Fetching episodes for: {Shows}", showLabel);
        var libraryResult = await _libraryService.GetEpisodesForShowsAsync(showNames, cancellationToken);

        if (libraryResult.Episodes.Count == 0)
        {
            var failedMsg = libraryResult.FailedShows.Count > 0
                ? $"Series not found: {string.Join(", ", libraryResult.FailedShows)}. Use exact series names from your Jellyfin library."
                : "No episodes found in the matched series.";

            return new GenerateResponse
            {
                Show = showLabel,
                Report = new ReportSummary
                {
                    TotalPlaylists = 0,
                    TotalTerms = 0,
                    TotalResolved = 0,
                    TotalFailed = 1,
                    FailedLookups = [new FailedLookup { Playlist = "N/A", Term = failedMsg }]
                }
            };
        }

        _logger.LogInformation("Loaded {Count} total episodes from {ShowCount} show(s)",
            libraryResult.Episodes.Count, libraryResult.ResolvedShows.Count);

        // Step 2: Ask LLM to generate playlists
        var playlists = await _llmService.GeneratePlaylistsAsync(
            showLabel,
            libraryResult.Episodes,
            request.PlaylistCount,
            request.Instructions,
            cancellationToken);

        _logger.LogInformation("LLM generated {Count} playlist(s)", playlists.Count);

        // Step 3: Resolve episode IDs using the shared resolver
        var resolveResult = await _resolverService.ResolvePlaylistsAsync(
            playlists,
            libraryResult.Episodes,
            cancellationToken);

        // Step 4: Optionally push collections to Jellyfin
        if (request.Push)
        {
            _logger.LogInformation("Push flag set — creating collections on Jellyfin...");
            foreach (var (result, playlist) in resolveResult.Results.Zip(playlists))
            {
                if (result.Ids.Count > 0)
                {
                    try
                    {
                        var collectionId = await _jellyfinService.CreateCollectionAsync(result.Title, result.Ids, cancellationToken);
                        result.CollectionId = collectionId;
                        _logger.LogInformation("  Created: {Title} (ID: {Id})", result.Title, collectionId);

                        // Update metadata with description and tagline
                        if (collectionId is not null && (playlist.Description is not null || playlist.Tagline is not null))
                        {
                            await _jellyfinService.UpdateCollectionMetadataAsync(
                                collectionId,
                                result.Title,
                                playlist.Description,
                                playlist.Tagline,
                                cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Error = $"Push failed: {ex.Message}";
                        _logger.LogError(ex, "  Failed to push: {Title}", result.Title);
                    }
                }
            }
        }

        return new GenerateResponse
        {
            Show = showLabel,
            PlaylistsGenerated = playlists,
            Results = resolveResult.Results,
            Report = resolveResult.Report
        };
    }
}
