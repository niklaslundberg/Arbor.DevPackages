using Arbor.DevPackages.Core.Feeds;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Arbor.DevPackages.Server.Tests.ServiceIndex;

public sealed class ServiceIndexTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ServiceIndexTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetServiceIndex_ReturnsOkWithCorrectVersionAndResources()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/index.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("version").GetString().Should().Be("3.0.0");
        root.GetProperty("resources").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetServiceIndex_ResourceIds_ContainRequestHost()
    {
        var client = _factory.CreateClient();
        var expectedHost = client.BaseAddress!.Host;

        var response = await client.GetAsync("/feeds/default/v3/index.json");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var resources = doc.RootElement.GetProperty("resources");

        foreach (var resource in resources.EnumerateArray())
        {
            var id = resource.GetProperty("@id").GetString();
            id.Should().Contain(expectedHost);
        }
    }

    [Fact]
    public async Task GetServiceIndex_ContentType_IsApplicationJson()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/index.json");

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GetServiceIndex_UnknownFeed_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/feeds/nonexistent-feed/v3/index.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ServiceIndex_PerFeed_ResourcesPointToCorrectFeedPath()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/index.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var resources = doc.RootElement.GetProperty("resources");

        foreach (var resource in resources.EnumerateArray())
        {
            var id = resource.GetProperty("@id").GetString();
            id.Should().Contain("/feeds/default/v3/",
                because: "all resource URLs must be scoped under the feed's path segment");
        }
    }

    [Fact]
    public async Task GetServiceIndex_NuGetProtocolClient_CanLoadServiceIndex()
    {
        // Use a real Kestrel listener on a random port so NuGet.Protocol can
        // connect via its own HTTP stack without any handler injection.
        // ContentRootPath is set to a directory without appsettings.json so the
        // Server project's Kestrel endpoint config does not override UseUrls.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.GetTempPath() });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        Arbor.DevPackages.ServiceDefaults.Extensions.AddServiceDefaults(builder);
        builder.Services.AddSingleton<IFeedRouter>(
            new Arbor.DevPackages.Core.Feeds.FeedRouter(
                [new FeedConfiguration("default", new Uri("https://api.nuget.org/v3/flatcontainer"), AllowPrerelease: true)]));

        await using var app = builder.Build();
        Arbor.DevPackages.ServiceDefaults.Extensions.MapDefaultEndpoints(app);
        var feedsGroup = app.MapGroup("/feeds/{feedId}");
        Arbor.DevPackages.Server.ServiceIndex.ServiceIndexEndpoints.MapServiceIndex(feedsGroup);

        await app.StartAsync();

        var indexUrl = app.Urls.FirstOrDefault() is { } url
            ? $"{url}/feeds/default/v3/index.json"
            : throw new InvalidOperationException("The test server did not bind to any address.");

        var source = new PackageSource(indexUrl);
        var repository = Repository.Factory.GetCoreV3(source);

        var serviceIndex = await repository.GetResourceAsync<ServiceIndexResourceV3>(CancellationToken.None);

        serviceIndex.Should().NotBeNull();
        serviceIndex.GetServiceEntryUri("PackageBaseAddress/3.0.0").Should().NotBeNull();
        serviceIndex.GetServiceEntryUri("RegistrationsBaseUrl/3.6.0").Should().NotBeNull();
        serviceIndex.GetServiceEntryUri("SearchQueryService/3.5.0").Should().NotBeNull();

        await app.StopAsync();
    }

    [Fact]
    public async Task GetServiceIndex_PushEnabledFeed_IncludesPackagePublishResource()
    {
        using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IFeedRouter>(
                    new FeedRouter(
                        [new FeedConfiguration("local", AllowPush: true)]));
            }));

        var client = factory.CreateClient();

        var response = await client.GetAsync("/feeds/local/v3/index.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var resources = doc.RootElement.GetProperty("resources");

        bool hasPublishResource = false;
        foreach (var resource in resources.EnumerateArray())
        {
            if (resource.GetProperty("@type").GetString() == "PackagePublish/2.0.0")
            {
                hasPublishResource = true;
                var id = resource.GetProperty("@id").GetString();
                id.Should().Contain("/feeds/local/v3/push");
                break;
            }
        }

        hasPublishResource.Should().BeTrue(because: "a push-enabled feed must advertise PackagePublish/2.0.0");
    }

    [Fact]
    public async Task GetServiceIndex_ProxyFeed_DoesNotIncludePackagePublishResource()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/feeds/default/v3/index.json");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var resources = doc.RootElement.GetProperty("resources");

        bool hasPublishResource = false;
        foreach (var resource in resources.EnumerateArray())
        {
            if (resource.GetProperty("@type").GetString() == "PackagePublish/2.0.0")
            {
                hasPublishResource = true;
                break;
            }
        }

        hasPublishResource.Should().BeFalse(because: "a proxy feed must not advertise push");
    }
}
