using Microsoft.AspNetCore.Mvc;
using Shinerock.Application.DTOs;
using Shinerock.Application.UseCases;

namespace Shinerock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GenerateController : ControllerBase
{
    private readonly GenerateAndCreateCollectionsUseCase _generateAndCreate;
    private readonly ILogger<GenerateController> _logger;

    public GenerateController(GenerateAndCreateCollectionsUseCase generateAndCreate, ILogger<GenerateController> logger)
    {
        _generateAndCreate = generateAndCreate;
        _logger = logger;
    }

    /// <summary>
    /// Ask the LLM to generate themed playlists, then resolve episode IDs.
    /// Supports single show or multiple shows for cross-show playlists.
    /// </summary>
    /// <remarks>
    /// Single show:
    ///
    ///     POST /api/generate
    ///     { "show": "South Park", "playlistCount": 5, "instructions": "Butters episodes" }
    ///
    /// Multiple shows (cross-show playlists):
    ///
    ///     POST /api/generate
    ///     { "shows": ["South Park", "Futurama"], "playlistCount": 3, "instructions": "technology and science episodes" }
    ///
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(GenerateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest request, CancellationToken cancellationToken)
    {
        var showNames = request.GetShowNames();

        if (showNames.Count == 0)
        {
            return BadRequest(new { error = "Either 'show' (string) or 'shows' (array) is required." });
        }

        if (request.PlaylistCount < 1 || request.PlaylistCount > 20)
        {
            return BadRequest(new { error = "playlistCount must be between 1 and 20." });
        }

        _logger.LogInformation("Generate request: {Shows} ({Count} playlists)", string.Join(", ", showNames), request.PlaylistCount);

        var result = await _generateAndCreate.ExecuteAsync(request, cancellationToken);

        return Ok(result);
    }
}
