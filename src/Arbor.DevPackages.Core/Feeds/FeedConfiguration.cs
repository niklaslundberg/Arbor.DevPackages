namespace Arbor.DevPackages.Core.Feeds;

public sealed record FeedConfiguration(
    string FeedId,
    Uri? UpstreamUrl = null,
    bool AllowPrerelease = false,
    bool AllowPush = false,
    Uri? SearchUrl = null);
