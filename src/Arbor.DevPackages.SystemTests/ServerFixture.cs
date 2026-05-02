using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Arbor.DevPackages.SystemTests;

/// <summary>
/// Starts a real <c>Arbor.DevPackages.Server</c> process and waits for it to become healthy.
/// Configure properties before calling <see cref="InitializeAsync"/>; dispose kills the process
/// and deletes temp directories.
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    private const int HealthCheckTimeoutSeconds = 30;

    private Process? _process;
    private string? _storeDirectory;
    private readonly object _outputLock = new();
    private readonly System.Text.StringBuilder _serverOutput = new();
    private readonly System.Text.StringBuilder _serverError = new();

    /// <summary>Gets the base address of the started server (e.g. <c>http://localhost:54321</c>).</summary>
    public string BaseAddress { get; private set; } = null!;

    /// <summary>Gets the temporary package store directory used by this server instance.</summary>
    public string StoreDirectory => _storeDirectory!;

    /// <summary>Gets the TCP port the server is listening on.</summary>
    public int Port { get; private set; }

    // ── Configuration (set before InitializeAsync) ────────────────────────

    /// <summary>Upstream NuGet feed URL. Defaults to real nuget.org.</summary>
    public string UpstreamUrl { get; set; } = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// When <see langword="true"/>, pre-seeds the store from <c>testdata/serilog/</c>
    /// before starting the server.
    /// </summary>
    public bool PreSeedStore { get; set; }

    /// <summary>Whether to allow package push on the <c>nuget-org</c> feed.</summary>
    public bool AllowPush { get; set; }

    /// <summary>Whether to allow pre-release packages on the <c>nuget-org</c> feed.</summary>
    public bool AllowPrerelease { get; set; } = true;

    // ── IAsyncLifetime ────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        Port = GetFreePort();
        BaseAddress = $"http://localhost:{Port}";

        _storeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ArborDevPkg_SystemTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_storeDirectory);

        if (PreSeedStore)
        {
            SeedStore(_storeDirectory);
        }

        var serverExe = FindServerExecutable();

        var psi = new ProcessStartInfo
        {
            FileName = serverExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Inherit parent environment so PATH, DOTNET_ROOT, etc. are available,
        // then override server-specific settings.
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            psi.Environment[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        psi.Environment["ASPNETCORE_URLS"] = BaseAddress;
        psi.Environment["PackageStorePath"] = _storeDirectory;
        psi.Environment["Feeds__0__Id"] = "nuget-org";
        psi.Environment["Feeds__0__UpstreamUrl"] = UpstreamUrl;
        psi.Environment["Feeds__0__AllowPush"] = AllowPush ? "true" : "false";
        psi.Environment["Feeds__0__AllowPrerelease"] = AllowPrerelease ? "true" : "false";
        psi.Environment["DOTNET_ENVIRONMENT"] = "Development";
        // Suppress noisy startup logs in test output.
        psi.Environment["Logging__LogLevel__Default"] = "Warning";
        psi.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Arbor.DevPackages.Server process.");

        // Drain stdout and stderr asynchronously to avoid blocking the child process when
        // the pipe buffers fill up. The captured text is included in failure diagnostics.
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (_outputLock) _serverOutput.AppendLine(e.Data); } };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (_outputLock) _serverError.AppendLine(e.Data); } };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitForHealthyAsync();
    }

    public async Task DisposeAsync()
    {
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited — nothing to kill.
            }

            await _process.WaitForExitAsync();
            _process.Dispose();
            _process = null;
        }

        if (_storeDirectory is not null && Directory.Exists(_storeDirectory))
        {
            try
            {
                Directory.Delete(_storeDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; temp files will eventually be cleaned up by the OS.
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task WaitForHealthyAsync()
    {
        using var httpClient = new HttpClient();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(HealthCheckTimeoutSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"Server process exited unexpectedly with code {_process.ExitCode} " +
                    "before becoming healthy. " +
                    $"stdout: {_serverOutput} stderr: {_serverError}");
            }

            try
            {
                using var response = await httpClient.GetAsync($"{BaseAddress}/health");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Server not ready yet — keep polling.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new TimeoutException(
            $"Server at {BaseAddress}/health did not become healthy within {HealthCheckTimeoutSeconds} seconds. " +
            $"stdout: {_serverOutput} stderr: {_serverError}");
    }

    private static void SeedStore(string storeDirectory)
    {
        // Testdata is copied to the output directory under testdata/ by the .csproj Content item.
        var testdataDir = Path.Combine(AppContext.BaseDirectory, "testdata", "serilog");

        if (!Directory.Exists(testdataDir))
        {
            throw new DirectoryNotFoundException(
                $"Test-asset directory not found: '{testdataDir}'. " +
                "Ensure the testdata/serilog/ directory exists and contains the Serilog package assets.");
        }

        // Mirror the testdata/serilog/ subtree into {storeDirectory}/serilog/ so that
        // FileSystemPackageStore can find: serilog/{version}/serilog.{version}.{ext}
        CopyDirectoryTree(testdataDir, Path.Combine(storeDirectory, "serilog"));
    }

    private static void CopyDirectoryTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectoryTree(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// Locates the server executable from the test output directory.
    /// Handles both artifact layouts produced by <c>UseArtifactsOutput=true</c>:
    /// <list type="bullet">
    ///   <item><description><c>artifacts/bin/{project}/{config}/</c> — no TFM sub-folder (current layout)</description></item>
    ///   <item><description><c>artifacts/bin/{project}/{config}/{tfm}/</c> — with TFM sub-folder</description></item>
    /// </list>
    /// </summary>
    private static string FindServerExecutable()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var exeName = OperatingSystem.IsWindows()
            ? "Arbor.DevPackages.Server.exe"
            : "Arbor.DevPackages.Server";

        // Layout 1 (no TFM): AppContext.BaseDirectory = artifacts/bin/{project}/{config}/
        //   configName = last segment ("release" / "debug")
        //   binDir     = two levels up  → artifacts/bin/
        var configName1 = Path.GetFileName(baseDir);
        var binDir1 = Path.GetDirectoryName(Path.GetDirectoryName(baseDir));
        if (binDir1 is not null)
        {
            var candidate1 = Path.Combine(binDir1, "Arbor.DevPackages.Server", configName1, exeName);
            if (File.Exists(candidate1))
            {
                return candidate1;
            }
        }

        // Layout 2 (with TFM): AppContext.BaseDirectory = artifacts/bin/{project}/{config}/{tfm}/
        //   tfmName    = last segment ("net10.0")
        //   configName = second-to-last segment ("release" / "debug")
        //   binDir     = three levels up → artifacts/bin/
        var tfmName = Path.GetFileName(baseDir);
        var configParent = Path.GetDirectoryName(baseDir);
        if (configParent is not null)
        {
            var configName2 = Path.GetFileName(configParent);
            var binDir2 = Path.GetDirectoryName(Path.GetDirectoryName(configParent));
            if (binDir2 is not null)
            {
                var candidate2 = Path.Combine(binDir2, "Arbor.DevPackages.Server", configName2, tfmName, exeName);
                if (File.Exists(candidate2))
                {
                    return candidate2;
                }
            }
        }

        throw new FileNotFoundException(
            $"Server executable not found. Searched near '{AppContext.BaseDirectory}'. " +
            "Build the solution before running system tests.",
            exeName);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
