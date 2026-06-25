using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shinerock.Application.DTOs;
using Shinerock.Application.Interfaces;

namespace Shinerock.Application.UseCases;

/// <summary>
/// Uses AI to generate descriptions and taglines for existing collections that are missing them.
/// </summary>
public class EnrichCollectionsUseCase
{
    private readonly IJellyfinService _jellyfinService;
    private readonly ILlmService _llmService;
    private readonly ILogger<EnrichCollectionsUseCase> _logger;

    public EnrichCollectionsUseCase(
        IJellyfinService jellyfinService,
        ILlmService llmService,
        ILogger<EnrichCollectionsUseCase> logger)
    {
        _jellyfinService = jellyfinService;
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<List<EnrichResult>> ExecuteAsync(EnrichRequest request, CancellationToken cancellationToken = default)
    {
        // Step 1: Get all collections
        var allCollections = await _jellyfinService.GetAllCollectionsAsync(cancellationToken);
        _logger.LogInformation("Found {Count} collections on Jellyfin", allCollections.Count);

        // Filter to specific IDs if requested
        var targets = request.CollectionIds is { Count: > 0 }
            ? allCollections.Where(c => request.CollectionIds.Contains(c.Id)).ToList()
            : allCollections;

        // Filter to only those missing metadata (unless overwrite is set)
        if (!request.Overwrite)
        {
            targets = targets.Where(c => string.IsNullOrWhiteSpace(c.Overview)).ToList();
        }

        _logger.LogInformation("{Count} collections to enrich", targets.Count);

        var results = new List<EnrichResult>();

        foreach (var collection in targets)
        {
            _logger.LogInformation("Enriching: {Name}", collection.Name);

            try
            {
                // Get the episodes in this collection for context
                var items = await _jellyfinService.GetCollectionItemsAsync(collection.Id, cancellationToken);
                var episodeNames = items.Select(e => e.Name).ToList();

                // Ask LLM to generate description and tagline
                var (description, tagline) = await _llmService.GenerateCollectionMetadataAsync(
                    collection.Name,
                    episodeNames,
                    cancellationToken);

                // Write to Jellyfin
                await _jellyfinService.UpdateCollectionMetadataAsync(
                    collection.Id,
                    collection.Name,
                    description,
                    tagline,
                    cancellationToken);

                results.Add(new EnrichResult
                {
                    CollectionId = collection.Id,
                    Name = collection.Name,
                    Description = description,
                    Tagline = tagline,
                    Updated = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich: {Name}", collection.Name);
                results.Add(new EnrichResult
                {
                    CollectionId = collection.Id,
                    Name = collection.Name,
                    Updated = false,
                    Error = ex.Message
                });
            }
        }

        return results;
    }
}
