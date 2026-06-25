namespace Shinerock.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed configuration for OpenAI connection.
/// </summary>
public class OpenAiSettings
{
    public const string SectionName = "OpenAi";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
}
