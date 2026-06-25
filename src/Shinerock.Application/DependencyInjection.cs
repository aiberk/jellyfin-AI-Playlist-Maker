using Microsoft.Extensions.DependencyInjection;
using Shinerock.Application.Services;
using Shinerock.Application.UseCases;

namespace Shinerock.Application;

/// <summary>
/// Registers Application layer services into the DI container.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Shared services
        services.AddScoped<EpisodeResolverService>();
        services.AddScoped<LibraryService>();

        // Use cases
        services.AddScoped<CreatePlaylistCollectionsUseCase>();
        services.AddScoped<GenerateAndCreateCollectionsUseCase>();
        services.AddScoped<EnrichCollectionsUseCase>();

        return services;
    }
}
