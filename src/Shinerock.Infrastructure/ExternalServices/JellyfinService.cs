using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shinerock.Application.Interfaces;
using Shinerock.Infrastructure.Configuration;

namespace Shinerock.Infrastructure.ExternalServices;

/// <summary>
/// Implements IJellyfinService by calling the Jellyfin REST API.
/// </summary>
public class JellyfinService : IJellyfinService
{
    private readonly HttpClient _httpClient;
    private readonly JellyfinSettings _settings;
    private readonly ILogger<JellyfinService> _logger;

    public JellyfinService(HttpClient httpClient, IOptions<JellyfinSettings> settings, ILogger<JellyfinService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{_settings.ApiKey}\"");
    }

    public async Task<(string? Id, string? Name)> SearchSeriesAsync(string showName, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = $"/Items?recursive=true&searchTerm={Uri.EscapeDataString(showName)}&includeItemTypes=Series";
            var response = await _httpClient.GetAsync(query, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JellyfinSearchResponse>(cancellationToken: cancellationToken);

            if (result?.Items is { Count: > 0 })
            {
                var item = result.Items[0];
                return (item.Id, item.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for series: {ShowName}", showName);
        }

        return (null, null);
    }

    public async Task<List<EpisodeInfo>> GetAllEpisodesAsync(string seriesId, CancellationToken cancellationToken = default)
    {
        var allEpisodes = new List<EpisodeInfo>();

        try
        {
            // Get all seasons
            var seasonsResponse = await _httpClient.GetAsync($"/Shows/{seriesId}/Seasons", cancellationToken);
            seasonsResponse.EnsureSuccessStatusCode();
            var seasonsData = await seasonsResponse.Content.ReadFromJsonAsync<JellyfinSearchResponse>(cancellationToken: cancellationToken);

            if (seasonsData?.Items is null)
                return allEpisodes;

            // Get episodes for each season
            foreach (var season in seasonsData.Items)
            {
                var episodesResponse = await _httpClient.GetAsync(
                    $"/Shows/{seriesId}/Episodes?seasonId={season.Id}&fields=Overview",
                    cancellationToken);
                episodesResponse.EnsureSuccessStatusCode();

                var episodesData = await episodesResponse.Content.ReadFromJsonAsync<JellyfinEpisodeResponse>(cancellationToken: cancellationToken);

                if (episodesData?.Items is null)
                    continue;

                foreach (var ep in episodesData.Items)
                {
                    allEpisodes.Add(new EpisodeInfo
                    {
                        Id = ep.Id,
                        Name = ep.Name,
                        SeasonNumber = ep.ParentIndexNumber ?? 0,
                        EpisodeNumber = ep.IndexNumber ?? 0,
                        Overview = ep.Overview
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching episodes for series: {SeriesId}", seriesId);
        }

        return allEpisodes;
    }

    public async Task<(string? Id, string? Name)> SearchEpisodeAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = $"/Items?recursive=true&searchTerm={Uri.EscapeDataString(searchTerm)}&includeItemTypes=Episode";
            var response = await _httpClient.GetAsync(query, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JellyfinSearchResponse>(cancellationToken: cancellationToken);

            if (result?.Items is { Count: > 0 })
            {
                var item = result.Items[0];

                if (IsReasonableMatch(searchTerm, item.Name))
                {
                    return (item.Id, item.Name);
                }

                _logger.LogWarning("Jellyfin returned '{ResultName}' for search '{SearchTerm}' — rejected as poor match", item.Name, searchTerm);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for term: {Term}", searchTerm);
        }

        return (null, null);
    }

    private static bool IsReasonableMatch(string searchTerm, string resultName)
    {
        var search = searchTerm.Trim().ToLowerInvariant();
        var name = resultName.Trim().ToLowerInvariant();

        if (name.Contains(search) || search.Contains(name))
            return true;

        var searchWords = search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToArray();

        if (searchWords.Length == 0)
            return false;

        var matchedWords = searchWords.Count(w => name.Contains(w));
        var matchRatio = (double)matchedWords / searchWords.Length;

        return matchRatio >= 0.5;
    }

    public async Task<string?> CreateCollectionAsync(string name, IEnumerable<string> itemIds, CancellationToken cancellationToken = default)
    {
        var ids = string.Join(",", itemIds);
        var query = $"/Collections?name={Uri.EscapeDataString(name)}&ids={ids}";

        var response = await _httpClient.PostAsync(query, null, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JellyfinCollectionResponse>(cancellationToken: cancellationToken);
        return result?.Id;
    }

    public async Task UpdateCollectionMetadataAsync(string collectionId, string name, string? description = null, string? tagline = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["Id"] = collectionId,
                ["Name"] = name,
                ["Overview"] = description ?? "",
                ["Taglines"] = tagline is not null ? new[] { tagline } : Array.Empty<string>(),
                ["Tags"] = new[] { "ai-generated" },
                ["Genres"] = Array.Empty<string>(),
                ["Studios"] = Array.Empty<string>(),
                ["People"] = Array.Empty<string>(),
                ["ArtistItems"] = Array.Empty<string>(),
                ["AlbumArtists"] = Array.Empty<string>(),
                ["LockedFields"] = Array.Empty<string>(),
                ["LockData"] = false,
                ["ProviderIds"] = new Dictionary<string, string>()
            };

            var response = await _httpClient.PostAsJsonAsync($"/Items/{collectionId}", payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to update metadata for collection {Id}: HTTP {StatusCode}", collectionId, response.StatusCode);
            }
            else
            {
                _logger.LogInformation("Updated metadata for collection {Id}", collectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating metadata for collection {Id}", collectionId);
        }
    }

    public async Task AddToCollectionAsync(string collectionId, IEnumerable<string> itemIds, CancellationToken cancellationToken = default)
    {
        var ids = string.Join(",", itemIds);
        var response = await _httpClient.PostAsync($"/Collections/{collectionId}/Items?ids={ids}", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Added {Count} items to collection {Id}", itemIds.Count(), collectionId);
    }

    public async Task RemoveFromCollectionAsync(string collectionId, IEnumerable<string> itemIds, CancellationToken cancellationToken = default)
    {
        var ids = string.Join(",", itemIds);
        var response = await _httpClient.DeleteAsync($"/Collections/{collectionId}/Items?ids={ids}", cancellationToken);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Removed {Count} items from collection {Id}", itemIds.Count(), collectionId);
    }

    public async Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/Items/{collectionId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Deleted collection {Id}", collectionId);
    }

    public async Task<List<Application.Interfaces.CollectionInfo>> GetAllCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var collections = new List<Application.Interfaces.CollectionInfo>();

        try
        {
            var response = await _httpClient.GetAsync(
                "/Items?recursive=true&includeItemTypes=BoxSet&fields=Overview,Tags,Taglines",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JellyfinCollectionListResponse>(cancellationToken: cancellationToken);

            if (result?.Items is not null)
            {
                foreach (var item in result.Items)
                {
                    collections.Add(new Application.Interfaces.CollectionInfo
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Overview = item.Overview,
                        Taglines = item.Taglines ?? [],
                        Tags = item.Tags ?? []
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching collections");
        }

        return collections;
    }

    public async Task<List<EpisodeInfo>> GetCollectionItemsAsync(string collectionId, CancellationToken cancellationToken = default)
    {
        var items = new List<EpisodeInfo>();

        try
        {
            var response = await _httpClient.GetAsync(
                $"/Items?parentId={collectionId}&fields=Overview",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JellyfinEpisodeResponse>(cancellationToken: cancellationToken);

            if (result?.Items is not null)
            {
                foreach (var ep in result.Items)
                {
                    items.Add(new EpisodeInfo
                    {
                        Id = ep.Id,
                        Name = ep.Name,
                        SeasonNumber = ep.ParentIndexNumber ?? 0,
                        EpisodeNumber = ep.IndexNumber ?? 0,
                        Overview = ep.Overview
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching items for collection {Id}", collectionId);
        }

        return items;
    }
}

// --- Internal JSON models for Jellyfin responses ---

internal class JellyfinSearchResponse
{
    [JsonPropertyName("Items")]
    public List<JellyfinItem> Items { get; set; } = [];

    [JsonPropertyName("TotalRecordCount")]
    public int TotalRecordCount { get; set; }
}

internal class JellyfinItem
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;
}

internal class JellyfinEpisodeResponse
{
    [JsonPropertyName("Items")]
    public List<JellyfinEpisodeItem> Items { get; set; } = [];

    [JsonPropertyName("TotalRecordCount")]
    public int TotalRecordCount { get; set; }
}

internal class JellyfinEpisodeItem
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("IndexNumber")]
    public int? IndexNumber { get; set; }

    [JsonPropertyName("ParentIndexNumber")]
    public int? ParentIndexNumber { get; set; }

    [JsonPropertyName("Overview")]
    public string? Overview { get; set; }
}

internal class JellyfinCollectionResponse
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;
}

internal class JellyfinCollectionListResponse
{
    [JsonPropertyName("Items")]
    public List<JellyfinCollectionListItem> Items { get; set; } = [];
}

internal class JellyfinCollectionListItem
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("Taglines")]
    public List<string>? Taglines { get; set; }

    [JsonPropertyName("Tags")]
    public List<string>? Tags { get; set; }
}
