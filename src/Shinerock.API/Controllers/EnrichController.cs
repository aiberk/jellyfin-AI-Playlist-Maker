using Microsoft.AspNetCore.Mvc;
using Shinerock.Application.DTOs;
using Shinerock.Application.Interfaces;
using Shinerock.Application.UseCases;

namespace Shinerock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EnrichController : ControllerBase
{
    private readonly EnrichCollectionsUseCase _enrichUseCase;
    private readonly IJellyfinService _jellyfinService;
    private readonly ILogger<EnrichController> _logger;

    public EnrichController(
        EnrichCollectionsUseCase enrichUseCase,
        IJellyfinService jellyfinService,
        ILogger<EnrichController> logger)
    {
        _enrichUseCase = enrichUseCase;
        _jellyfinService = jellyfinService;
        _logger = logger;
    }

    /// <summary>
    /// AI batch: Generate descriptions and taglines for collections missing them.
    /// </summary>
    /// <remarks>
    /// Sample requests:
    ///
    ///     POST /api/enrich
    ///     {}
    ///
    ///     POST /api/enrich
    ///     { "overwrite": true, "collectionIds": ["abc123", "def456"] }
    ///
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(List<EnrichResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Enrich([FromBody] EnrichRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Enrich request: overwrite={Overwrite}, targets={Count}",
            request.Overwrite, request.CollectionIds?.Count ?? 0);

        var results = await _enrichUseCase.ExecuteAsync(request, cancellationToken);

        return Ok(new
        {
            enriched = results.Count(r => r.Updated),
            failed = results.Count(r => !r.Updated),
            results
        });
    }

    /// <summary>
    /// Manual batch: Update metadata for specific collections.
    /// </summary>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /api/enrich/manual
    ///     [
    ///       { "collectionId": "abc123", "description": "My custom description", "tagline": "My tagline" },
    ///       { "collectionId": "def456", "description": "Another one" }
    ///     ]
    ///
    /// </remarks>
    [HttpPost("manual")]
    [ProducesResponseType(typeof(List<EnrichResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ManualUpdate([FromBody] List<MetadataUpdateItem> updates, CancellationToken cancellationToken)
    {
        if (updates is null || updates.Count == 0)
            return BadRequest(new { error = "Request body must be a non-empty array of metadata updates." });

        var results = new List<EnrichResult>();

        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.CollectionId))
            {
                results.Add(new EnrichResult { CollectionId = update.CollectionId, Updated = false, Error = "Missing collectionId" });
                continue;
            }

            try
            {
                // We need the collection name for the update call
                var collections = await _jellyfinService.GetAllCollectionsAsync(cancellationToken);
                var collection = collections.FirstOrDefault(c => c.Id == update.CollectionId);

                if (collection is null)
                {
                    results.Add(new EnrichResult { CollectionId = update.CollectionId, Updated = false, Error = "Collection not found" });
                    continue;
                }

                await _jellyfinService.UpdateCollectionMetadataAsync(
                    update.CollectionId,
                    collection.Name,
                    update.Description,
                    update.Tagline,
                    cancellationToken);

                results.Add(new EnrichResult
                {
                    CollectionId = update.CollectionId,
                    Name = collection.Name,
                    Description = update.Description,
                    Tagline = update.Tagline,
                    Updated = true
                });
            }
            catch (Exception ex)
            {
                results.Add(new EnrichResult { CollectionId = update.CollectionId, Updated = false, Error = ex.Message });
            }
        }

        return Ok(new
        {
            updated = results.Count(r => r.Updated),
            failed = results.Count(r => !r.Updated),
            results
        });
    }

    /// <summary>
    /// List all collections with their current metadata (for discovery).
    /// </summary>
    [HttpGet("collections")]
    [ProducesResponseType(typeof(List<CollectionInfo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCollections(CancellationToken cancellationToken)
    {
        var collections = await _jellyfinService.GetAllCollectionsAsync(cancellationToken);
        return Ok(collections);
    }
}
