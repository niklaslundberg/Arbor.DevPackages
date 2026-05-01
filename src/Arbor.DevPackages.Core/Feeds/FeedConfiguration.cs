namespace Arbor.DevPackages.Core.Feeds;

public sealed record FeedConfiguration(string FeedId, Uri UpstreamUrl, bool AllowPrerelease = false);
