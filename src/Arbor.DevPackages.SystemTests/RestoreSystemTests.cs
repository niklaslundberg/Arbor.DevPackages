using System.Net.Sockets;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.SystemTests;

/// <summary>
/// End-to-end system tests for the NuGet restore flow.
/// No fakes, mocks, or in-process substitutes — a real <c>Arbor.DevPackages.Server</c>
/// process is started and a real <c>dotnet restore</c> subprocess runs against it.
/// </summary>
public sealed class RestoreSystemTests
{
    // ── Scenario 1: cached package, upstream unreachable ─────────────────

    /// <summary>
    /// The store is pre-seeded with the Serilog package.
    /// The upstream is a port with nothing listening (genuine connection-refused,
    /// not a mock). The server must serve from its local store without ever
    /// attempting to contact the upstream.
    /// </summary>
    [Fact]
    public async Task Restore_ViaProxy_WhenPackageCachedAndUpstreamUnreachable_SucceedsWithoutCallingUpstream()
    {
        var unusedPort = GetUnusedPort();

        await using var fixture = new ServerFixture
        {
            UpstreamUrl = $"http://localhost:{unusedPort}",
            PreSeedStore = true,
        };
        await fixture.InitializeAsync();

        var result = await NuGetRestoreRunner.RunAsync(fixture.Port, TestContext.Current.CancellationToken);

        // The restore must succeed.
        result.ExitCode.Should().Be(
            0,
            because: $"dotnet restore should succeed when the package is cached. " +
                      $"stdout: {result.Output} stderr: {result.Error}");

        // Timing assertion: no TCP timeout (≈20 s) to a dead upstream should occur.
        result.Duration.Should().BeLessThan(
            TimeSpan.FromSeconds(25),
            because: "the server should serve from the local cache without waiting for an upstream TCP timeout");
    }

    // ── Scenario 2: package not cached, fetch from real nuget.org ────────

    /// <summary>
    /// The local store is empty and the upstream is real nuget.org.
    /// Requires outbound internet access — excluded in air-gapped CI by the
    /// <c>SystemTest_Online</c> trait.
    /// </summary>
    [Fact]
    [Trait("Category", "SystemTest_Online")]
    public async Task Restore_ViaProxy_WhenPackageNotCached_FetchesFromUpstreamAndSucceeds()
    {
        await using var fixture = new ServerFixture
        {
            UpstreamUrl = "https://api.nuget.org/v3/index.json",
            PreSeedStore = false,
        };
        await fixture.InitializeAsync();

        var result = await NuGetRestoreRunner.RunAsync(fixture.Port, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(
            0,
            because: $"dotnet restore should succeed when the upstream is reachable. " +
                      $"stdout: {result.Output} stderr: {result.Error}");

        // Verify the server cached the package locally after proxying it.
        var nupkgPath = Path.Combine(
            fixture.StoreDirectory,
            "serilog",
            NuGetRestoreRunner.SerilogVersion,
            $"serilog.{NuGetRestoreRunner.SerilogVersion}.nupkg");

        File.Exists(nupkgPath).Should().BeTrue(
            because: $"the server should have stored the proxied package at '{nupkgPath}'");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Binds a <see cref="TcpListener"/> to port 0, reads the OS-assigned port,
    /// then stops the listener — leaving the port unused but "known".
    /// </summary>
    private static int GetUnusedPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, port: 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
