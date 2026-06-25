namespace Shinerock.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed configuration for Jellyfin connection.
/// </summary>
public class JellyfinSettings
{
    public const string SectionName = "Jellyfin";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
