using Microsoft.Extensions.Logging;
using NSubstitute;
using Shinerock.Application.Interfaces;
using Shinerock.Application.Services;

namespace Shinerock.Tests.Services;

/// <summary>
/// Tests for LibraryService — fetching show/episode data from Jellyfin.
/// </summary>
public class LibraryServiceTests
{
    private readonly IJellyfinService _jellyfinService;
    private readonly LibraryService _libraryService;

    public LibraryServiceTests()
    {
        _jellyfinService = Substitute.For<IJellyfinService>();
        var logger = Substitute.For<ILogger<LibraryService>>();
        _libraryService = new LibraryService(_jellyfinService, logger);
    }

    [Fact]
    public async Task SingleShow_FetchesEpisodes()
    {
        _jellyfinService.SearchSeriesAsync("South Park", Arg.Any<CancellationToken>())
            .Returns(("series-1", "South Park"));

        _jellyfinService.GetAllEpisodesAsync("series-1", Arg.Any<CancellationToken>())
            .Returns(new List<EpisodeInfo>
            {
                new() { Id = "ep-1", Name = "Cartman Gets an Anal Probe", SeasonNumber = 1, EpisodeNumber = 1 },
                new() { Id = "ep-2", Name = "Volcano", SeasonNumber = 1, EpisodeNumber = 2 },
            });

        var result = await _libraryService.GetEpisodesForShowsAsync(["South Park"]);

        Assert.Equal(2, result.Episodes.Count);
        Assert.Single(result.ResolvedShows);
        Assert.Empty(result.FailedShows);
        Assert.All(result.Episodes, e => Assert.Equal("South Park", e.ShowName));
    }

    [Fact]
    public async Task MultiShow_CombinesEpisodes()
    {
        _jellyfinService.SearchSeriesAsync("South Park", Arg.Any<CancellationToken>())
            .Returns(("sp-id", "South Park"));
        _jellyfinService.SearchSeriesAsync("Futurama", Arg.Any<CancellationToken>())
            .Returns(("ft-id", "Futurama"));

        _jellyfinService.GetAllEpisodesAsync("sp-id", Arg.Any<CancellationToken>())
            .Returns(new List<EpisodeInfo>
            {
                new() { Id = "sp-1", Name = "Volcano", SeasonNumber = 1, EpisodeNumber = 2 },
            });
        _jellyfinService.GetAllEpisodesAsync("ft-id", Arg.Any<CancellationToken>())
            .Returns(new List<EpisodeInfo>
            {
                new() { Id = "ft-1", Name = "Rebirth", SeasonNumber = 6, EpisodeNumber = 1 },
            });

        var result = await _libraryService.GetEpisodesForShowsAsync(["South Park", "Futurama"]);

        Assert.Equal(2, result.Episodes.Count);
        Assert.Equal(2, result.ResolvedShows.Count);
        Assert.Empty(result.FailedShows);
    }

    [Fact]
    public async Task ShowNotFound_TracksFailure()
    {
        _jellyfinService.SearchSeriesAsync("Nonexistent Show", Arg.Any<CancellationToken>())
            .Returns((null as string, null as string));

        var result = await _libraryService.GetEpisodesForShowsAsync(["Nonexistent Show"]);

        Assert.Empty(result.Episodes);
        Assert.Empty(result.ResolvedShows);
        Assert.Single(result.FailedShows);
        Assert.Contains("Nonexistent Show", result.FailedShows);
    }

    [Fact]
    public async Task PartialFailure_ReturnsWhatWorked()
    {
        _jellyfinService.SearchSeriesAsync("South Park", Arg.Any<CancellationToken>())
            .Returns(("sp-id", "South Park"));
        _jellyfinService.SearchSeriesAsync("Fake Show", Arg.Any<CancellationToken>())
            .Returns((null as string, null as string));

        _jellyfinService.GetAllEpisodesAsync("sp-id", Arg.Any<CancellationToken>())
            .Returns(new List<EpisodeInfo>
            {
                new() { Id = "sp-1", Name = "Volcano", SeasonNumber = 1, EpisodeNumber = 2 },
            });

        var result = await _libraryService.GetEpisodesForShowsAsync(["South Park", "Fake Show"]);

        Assert.Single(result.Episodes);
        Assert.Single(result.ResolvedShows);
        Assert.Single(result.FailedShows);
    }
}
