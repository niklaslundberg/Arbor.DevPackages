using System.IO.Compression;
using System.Net;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.SystemTests;

/// <summary>
/// End-to-end system tests for the NuGet package push flow.
/// No fakes, mocks, or in-process substitutes — a real <c>Arbor.DevPackages.Server</c>
/// process is started and real <c>dotnet nuget push</c> / HTTP subprocesses run against it.
/// </summary>
public sealed class PushSystemTests
{
    private const string PushTestPackageId = "SystemTestPackage";
    private const string PushTestPackageVersion = "1.0.0";

    // ── Scenario: push a new package to an AllowPush feed ─────────────────

    [Fact]
    public async Task Push_NewPackage_ToAllowPushFeed_SucceedsAndPackageIsServable()
    {
        await using var fixture = new ServerFixture
        {
            AllowPush = true,
            AllowPrerelease = false,
            // No upstream needed — we are only pushing, not proxying.
            UpstreamUrl = "http://localhost:1", // dead URL; push path never touches upstream
        };
        await fixture.InitializeAsync();

        var nupkgPath = CreateMinimalNupkgFile(PushTestPackageId, PushTestPackageVersion);

        try
        {
            var pushResult = await NuGetPushRunner.RunAsync(fixture.Port, nupkgPath);

            pushResult.ExitCode.Should().Be(
                0,
                because: $"dotnet nuget push should succeed for a new package. " +
                          $"stdout: {pushResult.Output} stderr: {pushResult.Error}");

            // Verify the package is now servable via the flat container endpoint.
            using var http = new HttpClient();
            var versionListUrl =
                $"{fixture.BaseAddress}/feeds/nuget-org/v3/flatcontainer/" +
                $"{PushTestPackageId.ToLowerInvariant()}/index.json";

            var response = await http.GetAsync(versionListUrl);
            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                because: "the pushed package should appear in the flat container version list");
        }
        finally
        {
            if (File.Exists(nupkgPath))
            {
                File.Delete(nupkgPath);
            }
        }
    }

    // ── Scenario: push a duplicate package returns 409 ────────────────────

    [Fact]
    public async Task Push_DuplicatePackage_ToAllowPushFeed_Returns409()
    {
        await using var fixture = new ServerFixture
        {
            AllowPush = true,
            UpstreamUrl = "http://localhost:1",
        };
        await fixture.InitializeAsync();

        var nupkgPath = CreateMinimalNupkgFile(PushTestPackageId, PushTestPackageVersion);

        try
        {
            // First push — must succeed.
            var firstPush = await NuGetPushRunner.RunAsync(fixture.Port, nupkgPath);
            firstPush.ExitCode.Should().Be(
                0,
                because: $"first push should succeed. stdout: {firstPush.Output} stderr: {firstPush.Error}");

            // Second push of the same package — must fail with a non-zero exit code
            // (the NuGet CLI maps HTTP 409 to a non-zero exit code).
            var secondPush = await NuGetPushRunner.RunAsync(fixture.Port, nupkgPath);
            secondPush.ExitCode.Should().NotBe(
                0,
                because: "pushing a duplicate package should fail (HTTP 409 → non-zero exit code)");
        }
        finally
        {
            if (File.Exists(nupkgPath))
            {
                File.Delete(nupkgPath);
            }
        }
    }

    // ── Scenario: push to a feed where AllowPush=false returns 403 ────────

    [Fact]
    public async Task Push_NewPackage_ToReadOnlyFeed_Fails()
    {
        await using var fixture = new ServerFixture
        {
            AllowPush = false,
            UpstreamUrl = "http://localhost:1",
        };
        await fixture.InitializeAsync();

        var nupkgPath = CreateMinimalNupkgFile(PushTestPackageId, PushTestPackageVersion);

        try
        {
            var pushResult = await NuGetPushRunner.RunAsync(fixture.Port, nupkgPath);

            // The NuGet CLI returns a non-zero exit code when the server rejects with 403.
            pushResult.ExitCode.Should().NotBe(
                0,
                because: $"pushing to a read-only feed should fail. " +
                          $"stdout: {pushResult.Output} stderr: {pushResult.Error}");
        }
        finally
        {
            if (File.Exists(nupkgPath))
            {
                File.Delete(nupkgPath);
            }
        }
    }

    // ── Scenario: push then restore round-trip ─────────────────────────────

    [Fact]
    public async Task Push_ThenRestore_RoundTrip_Succeeds()
    {
        const string roundTripId = "RoundTripPackage";
        const string roundTripVersion = "2.0.0";

        await using var fixture = new ServerFixture
        {
            AllowPush = true,
            UpstreamUrl = "http://localhost:1", // upstream unreachable; restore must use local store
        };
        await fixture.InitializeAsync();

        var nupkgPath = CreateMinimalNupkgFile(roundTripId, roundTripVersion);

        try
        {
            // Push the package.
            var pushResult = await NuGetPushRunner.RunAsync(fixture.Port, nupkgPath);
            pushResult.ExitCode.Should().Be(
                0,
                because: $"push must succeed before we can test restore. " +
                          $"stdout: {pushResult.Output} stderr: {pushResult.Error}");

            // Restore using a custom runner that targets the specific package.
            var restoreResult = await NuGetRestoreRunner.RunForPackageAsync(
                fixture.Port, roundTripId, roundTripVersion);

            restoreResult.ExitCode.Should().Be(
                0,
                because: $"dotnet restore should succeed for a package that was just pushed. " +
                          $"stdout: {restoreResult.Output} stderr: {restoreResult.Error}");
        }
        finally
        {
            if (File.Exists(nupkgPath))
            {
                File.Delete(nupkgPath);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal valid .nupkg file (ZIP containing a .nuspec) in a temp location.
    /// The caller is responsible for deleting it.
    /// </summary>
    private static string CreateMinimalNupkgFile(string packageId, string version)
    {
        var nuspecContent =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{packageId}</id>
                <version>{version}</version>
                <authors>SystemTest</authors>
                <description>Minimal package created by Arbor.DevPackages system tests.</description>
              </metadata>
            </package>
            """;

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry($"{packageId.ToLowerInvariant()}.{version}.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(nuspecContent);
        }

        var tmpPath = Path.Combine(
            Path.GetTempPath(),
            $"{packageId.ToLowerInvariant()}.{version}.{Guid.NewGuid():N}.nupkg");

        File.WriteAllBytes(tmpPath, ms.ToArray());
        return tmpPath;
    }
}
