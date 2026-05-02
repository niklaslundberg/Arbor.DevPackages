namespace Arbor.DevPackages.Core.Feeds;

public interface IFeedRouter
{
    Task<FeedConfiguration?> RouteAsync(string feedId, CancellationToken cancellationToken);
}
