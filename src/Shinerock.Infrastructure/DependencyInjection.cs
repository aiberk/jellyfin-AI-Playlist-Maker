using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shinerock.Application.Interfaces;
using Shinerock.Infrastructure.Configuration;
using Shinerock.Infrastructure.ExternalServices;

namespace Shinerock.Infrastructure;

/// <summary>
/// Registers Infrastructure layer services into the DI container.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind Jellyfin settings from appsettings.json
        services.Configure<JellyfinSettings>(configuration.GetSection(JellyfinSettings.SectionName));

        // Register Jellyfin HTTP client
        services.AddHttpClient<IJellyfinService, JellyfinService>();

        // Bind OpenAI settings from appsettings.json
        services.Configure<OpenAiSettings>(configuration.GetSection(OpenAiSettings.SectionName));

        // Register LLM service
        services.AddScoped<ILlmService, OpenAiLlmService>();

        return services;
    }
}
