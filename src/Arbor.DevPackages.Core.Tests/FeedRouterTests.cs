using Arbor.DevPackages.Core.Feeds;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests;

public class FeedRouterTests
{
    private static readonly Uri NuGetOrg = new("https://api.nuget.org/v3/flatcontainer");

    [Fact]
    public async Task RouteAsync_KnownFeedId_ReturnsFeedConfiguration()
    {
        var feed = new FeedConfiguration("nuget-org", NuGetOrg, AllowPrerelease: false);
        var router = new FeedRouter([feed]);

        var result = await router.RouteAsync("nuget-org", CancellationToken.None);

        result.Should().NotBeNull();
        result!.FeedId.Should().Be("nuget-org");
        result.UpstreamUrl.Should().Be(NuGetOrg);
    }

    [Fact]
    public async Task RouteAsync_KnownFeedId_CaseInsensitive()
    {
        var feed = new FeedConfiguration("MyFeed", NuGetOrg);
        var router = new FeedRouter([feed]);

        var result = await router.RouteAsync("myfeed", CancellationToken.None);

        result.Should().NotBeNull();
        result!.FeedId.Should().Be("MyFeed");
    }

    [Fact]
    public async Task RouteAsync_UnknownFeedId_ReturnsNull()
    {
        var feed = new FeedConfiguration("nuget-org", NuGetOrg);
        var router = new FeedRouter([feed]);

        var result = await router.RouteAsync("nonexistent", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RouteAsync_MultipleFeedsRegistered_ReturnsCorrectFeed()
    {
        var feeds = new[]
        {
            new FeedConfiguration("feed-a", new Uri("https://feed-a.example.com/"), AllowPrerelease: true),
            new FeedConfiguration("feed-b", new Uri("https://feed-b.example.com/"), AllowPrerelease: false)
        };
        var router = new FeedRouter(feeds);

        var resultA = await router.RouteAsync("feed-a", CancellationToken.None);
        var resultB = await router.RouteAsync("feed-b", CancellationToken.None);

        resultA.Should().NotBeNull();
        resultA!.AllowPrerelease.Should().BeTrue();

        resultB.Should().NotBeNull();
        resultB!.AllowPrerelease.Should().BeFalse();
    }
}
