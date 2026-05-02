using Arbor.DevPackages.Core.Feeds;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests;

public class FeedConfigurationTests
{
    [Fact]
    public void FeedConfiguration_AllowPrerelease_DefaultsToFalse()
    {
        var config = new FeedConfiguration("nuget-org", new Uri("https://api.nuget.org/v3/index.json"));

        config.AllowPrerelease.Should().BeFalse();
    }

    [Fact]
    public void FeedConfiguration_AllowPush_DefaultsToFalse()
    {
        var config = new FeedConfiguration("nuget-org", new Uri("https://api.nuget.org/v3/index.json"));

        config.AllowPush.Should().BeFalse();
    }

    [Fact]
    public void FeedConfiguration_LocalFeed_CanHaveNullUpstreamUrl()
    {
        var config = new FeedConfiguration("local", AllowPush: true);

        config.UpstreamUrl.Should().BeNull();
        config.AllowPush.Should().BeTrue();
    }

    [Fact]
    public void FeedConfiguration_ProxyFeed_HasUpstreamUrl()
    {
        var upstreamUrl = new Uri("https://api.nuget.org/v3/flatcontainer");
        var config = new FeedConfiguration("proxy", upstreamUrl);

        config.UpstreamUrl.Should().Be(upstreamUrl);
        config.AllowPush.Should().BeFalse();
    }
}
