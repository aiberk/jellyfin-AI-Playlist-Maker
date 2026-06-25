namespace Shinerock.Application.Interfaces;

/// <summary>
/// Abstraction over Jellyfin API operations.
/// Implementation lives in Infrastructure.
/// </summary>
public interface IJellyfinService
{
    /// <summary>
    /// Search for a series/show by name and return its ID.
    /// </summary>
    Task<(string? Id, string? Name)> SearchSeriesAsync(string showName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all episodes for a series, organized by season.
    /// Returns a flat list of episode info (season number, episode number, name, ID).
    /// </summary>
    Task<List<EpisodeInfo>> GetAllEpisodesAsync(string seriesId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search Jellyfin for an episode by term and return the first match's ID.
    /// </summary>
    Task<(string? Id, string? Name)> SearchEpisodeAsync(string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a collection on Jellyfin with the given name and item IDs.
    /// Returns the new collection's ID.
    /// </summary>
    Task<string?> CreateCollectionAsync(string name, IEnumerable<string> itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a collection's metadata (description, tagline, tags).
    /// </summary>
    Task UpdateCollectionMetadataAsync(string collectionId, string name, string? description = null, string? tagline = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add items (episodes) to an existing collection.
    /// </summary>
    Task AddToCollectionAsync(string collectionId, IEnumerable<string> itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove items from a collection.
    /// </summary>
    Task RemoveFromCollectionAsync(string collectionId, IEnumerable<string> itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a collection entirely.
    /// </summary>
    Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all collections (BoxSets) on the server with their current metadata.
    /// </summary>
    Task<List<CollectionInfo>> GetAllCollectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the items (episodes) inside a collection.
    /// </summary>
    Task<List<EpisodeInfo>> GetCollectionItemsAsync(string collectionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a collection from the Jellyfin library.
/// </summary>
public class CollectionInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Overview { get; set; }
    public List<string> Taglines { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Represents a single episode from the Jellyfin library.
/// </summary>
public class EpisodeInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShowName { get; set; } = string.Empty;
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string? Overview { get; set; }
}
