using System.Net;
using System.Text;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Statistics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arbor.DevPackages.Server.StartPage;

public static class StartPageEndpoints
{
    public static IEndpointRouteBuilder MapStartPage(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", GetStartPageAsync);
        return app;
    }

    private static async Task<IResult> GetStartPageAsync(
        IReadOnlyList<FeedConfiguration> feeds,
        IStatisticsReader statisticsReader,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var allStats = await statisticsReader.GetAllPackageStatsAsync(cancellationToken);
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
        return Results.Content(BuildHtml(feeds, allStats, baseUrl), "text/html; charset=utf-8");
    }

    internal static string BuildHtml(
        IReadOnlyList<FeedConfiguration> feeds,
        IReadOnlyList<PackageStatsSummary> stats,
        string baseUrl)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("  <title>Arbor.DevPackages</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: sans-serif; max-width: 960px; margin: 2rem auto; padding: 0 1rem; color: #333; }");
        sb.AppendLine("    h1 { border-bottom: 2px solid #333; padding-bottom: 0.5rem; }");
        sb.AppendLine("    h2 { color: #555; margin-top: 2rem; border-bottom: 1px solid #ccc; padding-bottom: 0.3rem; }");
        sb.AppendLine("    .feed { margin-bottom: 1rem; padding: 1rem; border: 1px solid #ddd; border-radius: 4px; background: #fafafa; }");
        sb.AppendLine("    .feed h3 { margin: 0 0 0.5rem; }");
        sb.AppendLine("    code { background: #f0f0f0; padding: 0.15rem 0.4rem; border-radius: 3px; font-size: 0.9em; word-break: break-all; }");
        sb.AppendLine("    pre { background: #f0f0f0; padding: 0.75rem; border-radius: 3px; overflow-x: auto; font-size: 0.9em; }");
        sb.AppendLine("    table { border-collapse: collapse; width: 100%; margin-top: 0.5rem; }");
        sb.AppendLine("    th, td { border: 1px solid #ddd; padding: 0.5rem 0.75rem; text-align: left; }");
        sb.AppendLine("    th { background: #f5f5f5; }");
        sb.AppendLine("    .badge { display: inline-block; padding: 0.15rem 0.5rem; border-radius: 3px; font-size: 0.8em; }");
        sb.AppendLine("    .badge-yes { background: #d4edda; color: #155724; }");
        sb.AppendLine("    .badge-no { background: #f8d7da; color: #721c24; }");
        sb.AppendLine("    .empty { color: #888; font-style: italic; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<h1>Arbor.DevPackages</h1>");
        sb.AppendLine("<p>Local NuGet package proxy and cache server.</p>");

        // ── Feeds ──────────────────────────────────────────────────────────────
        sb.AppendLine("<h2>Configured Feeds</h2>");

        if (feeds.Count == 0)
        {
            sb.AppendLine("<p class=\"empty\">No feeds configured.</p>");
        }
        else
        {
            foreach (var feed in feeds)
            {
                var urlEncodedFeedId = Uri.EscapeDataString(feed.FeedId);
                var serviceIndexUrl = $"{baseUrl}/feeds/{urlEncodedFeedId}/v3/index.json";
                var escapedFeedId = WebUtility.HtmlEncode(feed.FeedId);
                var escapedServiceIndexUrl = WebUtility.HtmlEncode(serviceIndexUrl);

                sb.AppendLine("<div class=\"feed\">");
                sb.AppendLine($"  <h3>{escapedFeedId}</h3>");
                sb.AppendLine($"  <p><strong>NuGet Source URL:</strong> <code>{escapedServiceIndexUrl}</code></p>");
                sb.AppendLine("  <p>Add this feed to your NuGet configuration:</p>");
                sb.AppendLine($"  <pre>dotnet nuget add source {escapedServiceIndexUrl} --name {escapedFeedId}</pre>");

                if (feed.UpstreamUrl is not null)
                {
                    sb.AppendLine($"  <p><strong>Upstream:</strong> <code>{WebUtility.HtmlEncode(feed.UpstreamUrl.ToString())}</code></p>");
                }

                var allowPrerelease = feed.AllowPrerelease
                    ? "<span class=\"badge badge-yes\">Yes</span>"
                    : "<span class=\"badge badge-no\">No</span>";
                var allowPush = feed.AllowPush
                    ? "<span class=\"badge badge-yes\">Yes</span>"
                    : "<span class=\"badge badge-no\">No</span>";

                sb.AppendLine($"  <p><strong>Allow Pre-release:</strong> {allowPrerelease} &nbsp; <strong>Allow Push:</strong> {allowPush}</p>");
                sb.AppendLine("</div>");
            }
        }

        // ── Statistics ─────────────────────────────────────────────────────────
        sb.AppendLine("<h2>Statistics</h2>");

        if (stats.Count == 0)
        {
            sb.AppendLine("<p class=\"empty\">No downloads recorded yet.</p>");
        }
        else
        {
            sb.AppendLine("<table>");
            sb.AppendLine("  <thead>");
            sb.AppendLine("    <tr><th>Package</th><th>Version</th><th>Downloads</th><th>Last Downloaded</th></tr>");
            sb.AppendLine("  </thead>");
            sb.AppendLine("  <tbody>");

            foreach (var stat in stats)
            {
                var escapedId = WebUtility.HtmlEncode(stat.Identity.Id);
                var escapedVersion = WebUtility.HtmlEncode(stat.Identity.Version);
                var lastDownloaded = stat.LastDownloadedAt.HasValue
                    ? WebUtility.HtmlEncode(stat.LastDownloadedAt.Value.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                    : "<span class=\"empty\">\u2014</span>";

                sb.AppendLine($"    <tr><td>{escapedId}</td><td>{escapedVersion}</td><td>{stat.DownloadCount}</td><td>{lastDownloaded}</td></tr>");
            }

            sb.AppendLine("  </tbody>");
            sb.AppendLine("</table>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}
