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
}
