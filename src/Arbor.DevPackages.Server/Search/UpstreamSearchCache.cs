using System.Net.Http.Json;
using Arbor.DevPackages.Core.Feeds;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arbor.DevPackages.Server.Search;

/// <summary>
/// Background service that fetches and caches upstream NuGet search results in memory.
/// Refreshes on startup and every 30 minutes. When the upstream is unreachable,
/// the last successful results are retained (stale-if-error).
/// </summary>
public sealed class UpstreamSearchCache : BackgroundService, IUpstreamSearchCache
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Uri? _searchUrl;
    private readonly ILogger<UpstreamSearchCache> _logger;
    private volatile IReadOnlyList<SearchResultPackage>? _cachedEntries;

    public UpstreamSearchCache(
        IHttpClientFactory httpClientFactory,
        FeedConfiguration feed,
        ILogger<UpstreamSearchCache> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _searchUrl = feed.SearchUrl;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchResultPackage>? GetAllEntries() => _cachedEntries;

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_searchUrl is null)
        {
            return;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            var url = $"{_searchUrl.AbsoluteUri.TrimEnd('/')}?q=&take=1000&skip=0&prerelease=true&semVerLevel=2.0.0";
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SearchQueryResponse>(
                cancellationToken: cancellationToken);

            if (result?.Data is not null)
            {
                _cachedEntries = result.Data;
                _logger.LogInformation(
                    "Search cache refreshed; {Count} entries loaded.", result.Data.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Failed to refresh upstream search cache; retaining stale results.");
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_searchUrl is null)
        {
            return;
        }

        await RefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RefreshAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Unexpected error in search cache refresh loop.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — return cleanly.
        }
    }
}
