using Shinerock.Application.DTOs;

namespace Shinerock.Application.Interfaces;

/// <summary>
/// Abstraction over LLM operations.
/// Implementation lives in Infrastructure.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Given a show name, its full episode catalogue, and optional instructions, generate themed playlists.
    /// The LLM will only pick episodes from the provided list.
    /// </summary>
    Task<List<PlaylistRequest>> GeneratePlaylistsAsync(
        string showName,
        List<EpisodeInfo> availableEpisodes,
        int playlistCount = 5,
        string? instructions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a description and tagline for an existing collection based on its name and episodes.
    /// </summary>
    Task<(string Description, string Tagline)> GenerateCollectionMetadataAsync(
        string collectionName,
        List<string> episodeNames,
        CancellationToken cancellationToken = default);
}
