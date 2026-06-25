using Microsoft.Extensions.Logging;
using NSubstitute;
using Shinerock.Application.DTOs;
using Shinerock.Application.Interfaces;
using Shinerock.Application.Services;

namespace Shinerock.Tests.Services;

/// <summary>
/// Tests for EpisodeResolverService — the core matching logic.
/// </summary>
public class EpisodeResolverServiceTests
{
    private readonly IJellyfinService _jellyfinService;
    private readonly EpisodeResolverService _resolver;

    public EpisodeResolverServiceTests()
    {
        // NSubstitute creates a fake IJellyfinService — no real API calls
        _jellyfinService = Substitute.For<IJellyfinService>();
        var logger = Substitute.For<ILogger<EpisodeResolverService>>();
        _resolver = new EpisodeResolverService(_jellyfinService, logger);
    }

    [Fact]
    public async Task ExactMatch_ResolvesCorrectly()
    {
        // Arrange — set up known episodes
        var episodes = new List<EpisodeInfo>
        {
            new() { Id = "id-1", Name = "Scott Tenorman Must Die", SeasonNumber = 5, EpisodeNumber = 4 },
            new() { Id = "id-2", Name = "Casa Bonita", SeasonNumber = 7, EpisodeNumber = 11 },
        };

        var playlists = new List<PlaylistRequest>
        {
            new() { Title = "Test", Terms = ["Scott Tenorman Must Die"] }
        };

        // Act
        var result = await _resolver.ResolvePlaylistsAsync(playlists, episodes);

        // Assert
        Assert.Single(result.Results);
        Assert.Contains("id-1", result.Results[0].Ids);
        Assert.Empty(result.Results[0].FailedTerms);
        Assert.Equal(1, result.Report.TotalResolved);
        Assert.Equal(0, result.Report.TotalFailed);
    }

    [Fact]
    public async Task CaseInsensitiveMatch_Works()
    {
        var episodes = new List<EpisodeInfo>
        {
            new() { Id = "id-1", Name = "The Losing Edge", SeasonNumber = 9, EpisodeNumber = 5 },
        };

        var playlists = new List<PlaylistRequest>
        {
            new() { Title = "Test", Terms = ["the losing edge"] } // lowercase
        };

        var result = await _resolver.ResolvePlaylistsAsync(playlists, episodes);

        Assert.Contains("id-1", result.Results[0].Ids);
        Assert.Empty(result.Results[0].FailedTerms);
    }

    [Fact]
    public async Task ContainmentMatch_FindsPartialNames()
    {
        var episodes = new List<EpisodeInfo>
        {
            new() { Id = "id-1", Name = "Mr. Hankey's Christmas Classics", SeasonNumber = 3, EpisodeNumber = 15 },
        };

        var playlists = new List<PlaylistRequest>
        {
            new() { Title = "Test", Terms = ["Christmas Classics"] } // partial
        };

        var result = await _resolver.ResolvePlaylistsAsync(playlists, episodes);

        Assert.Contains("id-1", result.Results[0].Ids);
    }

    [Fact]
    public async Task UnmatchedTerm_FallsBackToFuzzySearch()
    {
        var episodes = new List<EpisodeInfo>
        {
            new() { Id = "id-1", Name = "Volcano", SeasonNumber = 1, EpisodeNumber = 2 },
        };

        // This term won't match anything in the catalogue
        var playlists = new List<PlaylistRequest>
        {
            new() { Title = "Test", Terms = ["Nonexistent Episode"] }
        };

        // Set up the fuzzy search fallback to also fail
        _jellyfinService.SearchEpisodeAsync("Nonexistent Episode", Arg.Any<CancellationToken>())
            .Returns((null as string, null as string));

        var result = await _resolver.ResolvePlaylistsAsync(playlists, episodes);

        Assert.Empty(result.Results[0].Ids);
        Assert.Contains("Nonexistent Episode", result.Results[0].FailedTerms);
        Assert.Equal(1, result.Report.TotalFailed);
    }

    [Fact]
    public async Task UnmatchedTerm_FuzzySearchSucceeds()
    {
        var episodes = new List<EpisodeInfo>
        {
            new() { Id = "id-1", Name = "Volcano", SeasonNumber = 1, EpisodeNumber = 2 },
        };

        var playlists = new List<PlaylistRequest>
        {
            new() { Title = "Test", Terms = ["Raisins"] } // not in catalogue
        };

        // Fuzzy search succeeds
        _jellyfinService.SearchEpisodeAsync("Raisins", Arg.Any<CancellationToken>())
            .Returns(("id-raisins", "Raisins"));

        var result = await _resolver.ResolvePlaylistsAsync(playlists, episodes);

        Assert.Contains("id-raisins", result.Results[0].Ids);
        Assert.Empty(result.Results[0].FailedTerms);
    }

    [Fact]
    public async Task NoKnownEpisodes_UsesOnlyFuzzySearch()
    {
        // No catalogue provided — everything goes through Jellyfin search
        var playlists = new List<PlaylistRequest>
        {
            new() { Title = "Test", Terms = ["Raisins", "Casa Bonita"] }
        };

        _jellyfinService.SearchEpisodeAsync("Raisins", Arg.Any<CancellationToken>())
            .Returns(("id-1", "Raisins"));
        _jellyfinService.SearchEpisodeAsync("Casa Bonita", Arg.Any<CancellationToken>())
            .Returns(("id-2", "Casa Bonita"));

        var result = await _resolver.ResolvePlaylistsAsync(playlists, knownEpisodes: null);

        Assert.Equal(2, result.Results[0].Ids.Count);
        Assert.Equal(2, result.Report.TotalResolved);
    }

    [Fact]
    public async Task MultiplePlaylistsReport_AggregatesCorrectly()
    {
        var episodes = new List<EpisodeInfo>
        {
            new() { Id = "id-1", Name = "Volcano", SeasonNumber = 1, EpisodeNumber = 2 },
            new() { Id = "id-2", Name = "Raisins", SeasonNumber = 7, EpisodeNumber = 14 },
        };

        _jellyfinService.SearchEpisodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((null as string, null as string));

        var playlists = new List<PlaylistRequest>
        {
            new() { Title = "Playlist 1", Terms = ["Volcano", "Fake Episode"] },
            new() { Title = "Playlist 2", Terms = ["Raisins"] }
        };

        var result = await _resolver.ResolvePlaylistsAsync(playlists, episodes);

        Assert.Equal(2, result.Report.TotalPlaylists);
        Assert.Equal(3, result.Report.TotalTerms);
        Assert.Equal(2, result.Report.TotalResolved);
        Assert.Equal(1, result.Report.TotalFailed);
        Assert.Single(result.Report.FailedLookups);
        Assert.Equal("Fake Episode", result.Report.FailedLookups[0].Term);
    }
}
