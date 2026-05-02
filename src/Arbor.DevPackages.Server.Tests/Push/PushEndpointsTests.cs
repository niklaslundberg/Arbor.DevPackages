using System.IO.Compression;
using System.Net;
using System.Text;
using Arbor.DevPackages.Core.Feeds;
using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Statistics;
using Arbor.DevPackages.Server.Push;
using Arbor.DevPackages.Server.ServiceIndex;
using Arbor.DevPackages.Testing;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Xunit;

namespace Arbor.DevPackages.Server.Tests.Push;

public sealed class PushEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly string TestNuspec =
        "<package><metadata><id>TestPackage</id><version>1.0.0</version></metadata></package>";

    private static readonly string TestPrereleaseNuspec =
        "<package><metadata><id>TestPackage</id><version>1.0.0-beta.1</version></metadata></package>";

    public PushEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> BuildFactory(
        InMemoryPackageStore? store = null,
        bool allowPush = true,
        bool allowPrerelease = false)
    {
        var packageStore = store ?? new InMemoryPackageStore();

        return _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IPackageStore>(packageStore);
                services.AddSingleton<IStatisticsCollector>(new RecordingStatisticsCollector());
                services.AddSingleton<IFeedRouter>(
                    new FeedRouter(
                        [new FeedConfiguration("default", AllowPush: allowPush, AllowPrerelease: allowPrerelease)]));
            }));
    }

    private static byte[] CreateNupkgBytes(string nuspecContent)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("testpackage.1.0.0.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(nuspecContent);
        }

        return ms.ToArray();
    }

    private static MultipartFormDataContent CreatePushContent(byte[] nupkgBytes)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(nupkgBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "package", "testpackage.1.0.0.nupkg");
        return content;
    }

    // ─── PushPackage_ValidPackage ─────────────────────────────────────────────

    [Fact]
    public async Task PushPackage_ValidPackage_Returns201AndStoresPackage()
    {
        var store = new InMemoryPackageStore();
        using var factory = BuildFactory(store, allowPush: true);
        var client = factory.CreateClient();

        var nupkgBytes = CreateNupkgBytes(TestNuspec);
        using var content = CreatePushContent(nupkgBytes);

        var response = await client.PutAsync("/feeds/default/v3/push", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var identity = new PackageIdentity("testpackage", "1.0.0");
        var stored = await store.ExistsAsync(identity, CancellationToken.None);
        stored.Should().BeTrue();
    }

    // ─── PushPackage_AlreadyExists ────────────────────────────────────────────

    [Fact]
    public async Task PushPackage_AlreadyExists_Returns409()
    {
        var store = new InMemoryPackageStore();
        store.Add(new PackageIdentity("testpackage", "1.0.0"), CreateNupkgBytes(TestNuspec), TestNuspec);

        using var factory = BuildFactory(store, allowPush: true);
        var client = factory.CreateClient();

        var nupkgBytes = CreateNupkgBytes(TestNuspec);
        using var content = CreatePushContent(nupkgBytes);

        var response = await client.PutAsync("/feeds/default/v3/push", content);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── PushPackage_FeedDisallowsPush ────────────────────────────────────────

    [Fact]
    public async Task PushPackage_FeedDisallowsPush_Returns403()
    {
        using var factory = BuildFactory(allowPush: false);
        var client = factory.CreateClient();

        var nupkgBytes = CreateNupkgBytes(TestNuspec);
        using var content = CreatePushContent(nupkgBytes);

        var response = await client.PutAsync("/feeds/default/v3/push", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── PushPackage_FeedNotFound ─────────────────────────────────────────────

    [Fact]
    public async Task PushPackage_FeedNotFound_Returns404()
    {
        using var factory = BuildFactory(allowPush: true);
        var client = factory.CreateClient();

        var nupkgBytes = CreateNupkgBytes(TestNuspec);
        using var content = CreatePushContent(nupkgBytes);

        var response = await client.PutAsync("/feeds/nonexistent/v3/push", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── PushPackage_PrereleaseRejected ───────────────────────────────────────

    [Fact]
    public async Task PushPackage_PrereleaseToNonPrereleaseEnabledFeed_Returns422()
    {
        using var factory = BuildFactory(allowPush: true, allowPrerelease: false);
        var client = factory.CreateClient();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("testpackage.1.0.0-beta.1.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(TestPrereleaseNuspec);
        }

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ms.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "package", "testpackage.1.0.0-beta.1.nupkg");

        var response = await client.PutAsync("/feeds/default/v3/push", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ─── PushPackage_PrereleaseAllowed ────────────────────────────────────────

    [Fact]
    public async Task PushPackage_PrereleaseToPrereleaseFeed_Returns201()
    {
        var store = new InMemoryPackageStore();
        using var factory = BuildFactory(store, allowPush: true, allowPrerelease: true);
        var client = factory.CreateClient();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("testpackage.1.0.0-beta.1.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(TestPrereleaseNuspec);
        }

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ms.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "package", "testpackage.1.0.0-beta.1.nupkg");

        var response = await client.PutAsync("/feeds/default/v3/push", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var identity = new PackageIdentity("testpackage", "1.0.0-beta.1");
        var stored = await store.ExistsAsync(identity, CancellationToken.None);
        stored.Should().BeTrue();
    }

    // ─── PushPackage_InvalidZip ───────────────────────────────────────────────

    [Fact]
    public async Task PushPackage_InvalidZip_Returns400()
    {
        using var factory = BuildFactory(allowPush: true);
        var client = factory.CreateClient();

        // Upload random bytes that are not a valid ZIP file.
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("this is not a zip file"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "package", "invalid.1.0.0.nupkg");

        var response = await client.PutAsync("/feeds/default/v3/push", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── PushPackage_NoFiles ──────────────────────────────────────────────────

    [Fact]
    public async Task PushPackage_NoFiles_Returns400()
    {
        using var factory = BuildFactory(allowPush: true);
        var client = factory.CreateClient();

        // Send multipart content without any files.
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("somevalue"), "notafile");

        var response = await client.PutAsync("/feeds/default/v3/push", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── PushPackage_NuGetProtocolClient_CanPushPackage ───────────────────────

    [Fact]
    public async Task PushPackage_NuGetProtocolClient_CanPushPackage()
    {
        // NuGet.Protocol's PackageUpdateResource works with file paths,
        // so we create a temporary nupkg file on disk.
        var store = new InMemoryPackageStore();
        const string pushNuspec =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>PushProtocolTest</id>
                <version>2.0.0</version>
                <authors>Test</authors>
                <description>Test package</description>
              </metadata>
            </package>
            """;

        // Build a minimal valid nupkg zip containing the nuspec.
        var nupkgBytes = CreateNupkgBytesWithNuspec("pushprotocoltest.2.0.0.nuspec", pushNuspec);

        // Write to a temp file so PackageUpdateResource can read it.
        var tmpNupkg = Path.Combine(Path.GetTempPath(), $"pushprotocoltest.2.0.0.{Guid.NewGuid():N}.nupkg");
        await File.WriteAllBytesAsync(tmpNupkg, nupkgBytes);

        try
        {
            // Start a real Kestrel server so NuGet.Protocol uses its own HTTP stack.
            // Push must come from loopback, so bind to 127.0.0.1.
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            Arbor.DevPackages.ServiceDefaults.Extensions.AddServiceDefaults(builder);
            builder.Services.AddSingleton<IPackageStore>(store);
            builder.Services.AddSingleton<IStatisticsCollector>(new RecordingStatisticsCollector());
            builder.Services.AddSingleton<IFeedRouter>(
                new FeedRouter(
                    [new FeedConfiguration("local", AllowPush: true)]));

            await using var app = builder.Build();
            Arbor.DevPackages.ServiceDefaults.Extensions.MapDefaultEndpoints(app);
            var feedsGroup = app.MapGroup("/feeds/{feedId}");
            ServiceIndexEndpoints.MapServiceIndex(feedsGroup);
            PushEndpoints.MapPush(feedsGroup);

            await app.StartAsync();

            var boundUrl = app.Urls.FirstOrDefault()
                ?? throw new InvalidOperationException("The test server did not bind to any address.");

            try
            {
                var indexUrl = $"{boundUrl}/feeds/local/v3/index.json";

                var source = new PackageSource(indexUrl);
                var repository = Repository.Factory.GetCoreV3(source);

                var pushResource = await repository.GetResourceAsync<PackageUpdateResource>(CancellationToken.None);

                await pushResource.Push(
                    packagePaths: [tmpNupkg],
                    symbolSource: null,
                    timeoutInSecond: 30,
                    disableBuffering: false,
                    getApiKey: _ => null,
                    getSymbolApiKey: _ => null,
                    noServiceEndpoint: false,
                    skipDuplicate: false,
                    symbolPackageUpdateResource: null,
                    allowInsecureConnections: true,
                    log: NullLogger.Instance);

                // Verify the package was stored.
                var identity = new PackageIdentity("pushprotocoltest", "2.0.0");
                var stored = await store.ExistsAsync(identity, CancellationToken.None);
                stored.Should().BeTrue(because: "PackageUpdateResource.Push should have stored the package");
            }
            finally
            {
                await app.StopAsync();
            }
        }
        finally
        {
            if (File.Exists(tmpNupkg))
            {
                File.Delete(tmpNupkg);
            }
        }
    }

    private static byte[] CreateNupkgBytesWithNuspec(string entryName, string nuspecContent)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(nuspecContent);
        }

        return ms.ToArray();
    }
}

/// <summary>
/// Unit tests for <see cref="PushEndpoints.ExtractPackageIdentity"/>.
/// </summary>
public sealed class ExtractPackageIdentityTests
{
    [Fact]
    public void ExtractPackageIdentity_StandardNuspec_ReturnsLowercasedIdAndOriginalVersion()
    {
        const string nuspec =
            "<package><metadata><id>MyPackage</id><version>1.2.3</version></metadata></package>";

        var identity = PushEndpoints.ExtractPackageIdentity(nuspec);

        identity.Id.Should().Be("mypackage");
        identity.Version.Should().Be("1.2.3");
    }

    [Fact]
    public void ExtractPackageIdentity_NuspecWithNamespace_ReturnsIdentity()
    {
        const string nuspec = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>SomePackage</id>
                <version>2.0.0</version>
              </metadata>
            </package>
            """;

        var identity = PushEndpoints.ExtractPackageIdentity(nuspec);

        identity.Id.Should().Be("somepackage");
        identity.Version.Should().Be("2.0.0");
    }

    [Fact]
    public void ExtractPackageIdentity_MissingId_ThrowsFormatException()
    {
        const string nuspec =
            "<package><metadata><version>1.0.0</version></metadata></package>";

        var act = () => PushEndpoints.ExtractPackageIdentity(nuspec);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ExtractPackageIdentity_MissingVersion_ThrowsFormatException()
    {
        const string nuspec =
            "<package><metadata><id>MyPackage</id></metadata></package>";

        var act = () => PushEndpoints.ExtractPackageIdentity(nuspec);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ExtractPackageIdentity_InvalidXml_ThrowsFormatException()
    {
        const string nuspec = "not valid xml <<>";

        var act = () => PushEndpoints.ExtractPackageIdentity(nuspec);

        act.Should().Throw<FormatException>();
    }
}
