using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Proxy;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests.Proxy;

public sealed class NoOpCredentialProviderTests
{
    [Fact]
    public async Task GetCredentialAsync_ReturnsNull()
    {
        var provider = new NoOpCredentialProvider();
        var feed = new FeedConfiguration("test", new Uri("https://example.com/v3/flatcontainer"));

        string? credential = await provider.GetCredentialAsync(feed, TestContext.Current.CancellationToken);

        credential.Should().BeNull();
    }
}
