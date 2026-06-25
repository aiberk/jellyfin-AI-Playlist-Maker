using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Shinerock.Application.DTOs;
using Shinerock.Application.Interfaces;
using Shinerock.Infrastructure.Configuration;

namespace Shinerock.Infrastructure.ExternalServices;

/// <summary>
/// Implements ILlmService using OpenAI's API with structured outputs (JSON schema).
/// The LLM receives the full episode list from Jellyfin and picks only real episodes.
/// </summary>
public class OpenAiLlmService : ILlmService
{
    private readonly OpenAiSettings _settings;
    private readonly ILogger<OpenAiLlmService> _logger;

    public OpenAiLlmService(IOptions<OpenAiSettings> settings, ILogger<OpenAiLlmService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<List<PlaylistRequest>> GeneratePlaylistsAsync(
        string showName,
        List<EpisodeInfo> availableEpisodes,
        int playlistCount = 5,
        string? instructions = null,
        CancellationToken cancellationToken = default)
    {
        var client = new OpenAIClient(_settings.ApiKey);
        var chatClient = client.GetChatClient(_settings.Model);

        var systemPrompt = BuildSystemPrompt(availableEpisodes);
        var userPrompt = BuildUserPrompt(showName, playlistCount, instructions);

        _logger.LogInformation("Sending request to OpenAI ({Model}) with {EpisodeCount} episodes as context...",
            _settings.Model, availableEpisodes.Count);

        // Define the JSON schema for structured output — the "Zod equivalent"
        var jsonSchema = ChatResponseFormat.CreateJsonSchemaFormat(
            jsonSchemaFormatName: "playlist_collection",
            jsonSchema: BinaryData.FromString(PlaylistCollectionSchema),
            jsonSchemaIsStrict: true);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            ResponseFormat = jsonSchema,
            Temperature = 0.7f
        };

        var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
        var content = response.Value.Content[0].Text;

        _logger.LogInformation("Received response from OpenAI, parsing structured output...");

        var parsed = JsonSerializer.Deserialize<PlaylistCollectionWrapper>(content, JsonOptions);

        if (parsed?.Playlists is null || parsed.Playlists.Count == 0)
        {
            _logger.LogWarning("LLM returned empty playlists");
            return [];
        }

        _logger.LogInformation("Successfully parsed {Count} playlists from LLM", parsed.Playlists.Count);
        return parsed.Playlists;
    }

    private static string BuildSystemPrompt(List<EpisodeInfo> episodes)
    {
        var episodeList = new StringBuilder();
        var currentShow = "";
        var currentSeason = -1;

        foreach (var ep in episodes.OrderBy(e => e.ShowName).ThenBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber))
        {
            if (ep.ShowName != currentShow)
            {
                currentShow = ep.ShowName;
                currentSeason = -1;
                episodeList.AppendLine($"\n=== {currentShow} ===");
            }

            if (ep.SeasonNumber != currentSeason)
            {
                currentSeason = ep.SeasonNumber;
                episodeList.AppendLine($"\n--- Season {currentSeason} ---");
            }
            episodeList.AppendLine($"  S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2} - {ep.Name}");
        }

        return $"""
            You are a TV show curator and playlist expert. Your job is to create themed episode
            playlists for TV shows. Each playlist should have a clear theme and contain episodes
            that fit that theme well.

            CRITICAL RULES:
            - You MUST ONLY use episode names from the list below. Do NOT invent or guess episode names.
            - Use the EXACT episode name as shown in the list (copy it character for character).
            - Each playlist should have 4-8 episodes.
            - Playlist titles should be descriptive and fun.
            - Don't repeat episodes across playlists.
            - Pick episodes that genuinely fit the theme based on your knowledge of the show(s).
            - Playlists CAN mix episodes from different shows if multiple shows are provided.

            AVAILABLE EPISODES IN THE USER'S LIBRARY:
            {episodeList}
            """;
    }

    private static string BuildUserPrompt(string showName, int playlistCount, string? instructions)
    {
        var prompt = $"Create {playlistCount} themed episode playlists for \"{showName}\" using ONLY episodes from the list I provided.";

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            prompt += $"\n\nAdditional instructions: {instructions}";
        }

        return prompt;
    }

    private const string PlaylistCollectionSchema = """
        {
            "type": "object",
            "properties": {
                "playlists": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "title": {
                                "type": "string",
                                "description": "The themed playlist title, prefixed with the show name"
                            },
                            "description": {
                                "type": "string",
                                "description": "A 1-2 sentence description of the playlist theme and what ties the episodes together"
                            },
                            "tagline": {
                                "type": "string",
                                "description": "A short catchy one-liner for the playlist (under 60 chars)"
                            },
                            "terms": {
                                "type": "array",
                                "items": {
                                    "type": "string",
                                    "description": "Exact episode name from the provided list"
                                },
                                "description": "Array of exact episode names that fit this playlist theme"
                            }
                        },
                        "required": ["title", "description", "tagline", "terms"],
                        "additionalProperties": false
                    },
                    "description": "Array of themed playlists"
                }
            },
            "required": ["playlists"],
            "additionalProperties": false
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<(string Description, string Tagline)> GenerateCollectionMetadataAsync(
        string collectionName,
        List<string> episodeNames,
        CancellationToken cancellationToken = default)
    {
        var client = new OpenAIClient(_settings.ApiKey);
        var chatClient = client.GetChatClient(_settings.Model);

        var episodeList = string.Join("\n", episodeNames.Select(e => $"  - {e}"));

        var systemPrompt = """
            You are a media librarian. Given a collection name and the episodes it contains,
            write a short description (1-2 sentences) and a catchy tagline (under 60 characters).
            The description should explain the theme that ties the episodes together.
            """;

        var userPrompt = $"""
            Collection: "{collectionName}"

            Episodes:
            {episodeList}

            Generate a description and tagline for this collection.
            """;

        var jsonSchema = ChatResponseFormat.CreateJsonSchemaFormat(
            jsonSchemaFormatName: "collection_metadata",
            jsonSchema: BinaryData.FromString(MetadataSchema),
            jsonSchemaIsStrict: true);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            ResponseFormat = jsonSchema,
            Temperature = 0.7f
        };

        var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
        var content = response.Value.Content[0].Text;

        var parsed = JsonSerializer.Deserialize<MetadataResponse>(content, JsonOptions);

        return (parsed?.Description ?? "", parsed?.Tagline ?? "");
    }

    private const string MetadataSchema = """
        {
            "type": "object",
            "properties": {
                "description": {
                    "type": "string",
                    "description": "1-2 sentence description of what ties the episodes together"
                },
                "tagline": {
                    "type": "string",
                    "description": "Short catchy one-liner under 60 characters"
                }
            },
            "required": ["description", "tagline"],
            "additionalProperties": false
        }
        """;
}

internal class MetadataResponse
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string Tagline { get; set; } = string.Empty;
}

internal class PlaylistCollectionWrapper
{
    [JsonPropertyName("playlists")]
    public List<PlaylistRequest> Playlists { get; set; } = [];
}
