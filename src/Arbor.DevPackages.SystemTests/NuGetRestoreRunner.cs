using System.Diagnostics;

namespace Arbor.DevPackages.SystemTests;

/// <summary>
/// Runs <c>dotnet restore</c> against the test server as a subprocess.
/// Each call is fully isolated: a fresh NuGet global packages cache and project directory
/// are created in a temp folder and cleaned up in a <c>finally</c> block after the restore completes.
/// </summary>
public sealed class NuGetRestoreRunner
{
    /// <summary>The pinned Serilog version used in all restore-based system tests.</summary>
    public const string SerilogVersion = "3.1.1";

    /// <summary>Result of a <c>dotnet restore</c> run.</summary>
    public sealed record RestoreResult(int ExitCode, string Output, string Error, TimeSpan Duration);

    /// <summary>
    /// Runs <c>dotnet restore</c> for a single <see cref="SerilogVersion"/> reference, routing all
    /// traffic through the test server at the given <paramref name="serverPort"/>.
    /// </summary>
    public static Task<RestoreResult> RunAsync(
        int serverPort,
        CancellationToken cancellationToken = default) =>
        RunForPackageAsync(serverPort, "Serilog", SerilogVersion, cancellationToken);

    /// <summary>
    /// Runs <c>dotnet restore</c> for a single package reference of the given
    /// <paramref name="packageId"/> and <paramref name="version"/>, routing all
    /// traffic through the test server at the given <paramref name="serverPort"/>.
    /// </summary>
    public static async Task<RestoreResult> RunForPackageAsync(
        int serverPort,
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"ArborDevPkg_Restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        try
        {
            var nugetCacheDir = Path.Combine(workDir, "nuget-cache");
            Directory.CreateDirectory(nugetCacheDir);

            WriteNuGetConfig(workDir, serverPort);
            WriteTestProject(workDir, packageId, version);

            var sw = Stopwatch.StartNew();
            var result = await RunDotnetRestoreAsync(workDir, nugetCacheDir, cancellationToken);
            sw.Stop();

            return new RestoreResult(result.ExitCode, result.Output, result.Error, sw.Elapsed);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private static void WriteNuGetConfig(string workDir, int serverPort)
    {
        // Only this server is allowed as a source — no fallback to nuget.org directly.
        var content =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="test-server" value="http://localhost:{serverPort}/feeds/nuget-org/v3/index.json"
                     allowInsecureConnections="true" />
              </packageSources>
            </configuration>
            """;

        File.WriteAllText(Path.Combine(workDir, "nuget.config"), content);
    }

    private static void WriteTestProject(string workDir, string packageId, string version)
    {
        var content =
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{packageId}" Version="{version}" />
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Combine(workDir, "TestProject.csproj"), content);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunDotnetRestoreAsync(
        string workDir,
        string nugetCacheDir,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "restore", "TestProject.csproj", "--no-cache" },
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Isolate the global packages cache so each test run starts fresh.
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            psi.Environment[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        psi.Environment["NUGET_PACKAGES"] = nugetCacheDir;
        // Suppress telemetry noise.
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["NUGET_XMLDOC_MODE"] = "skip";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'dotnet restore' process.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode, output, error);
    }
}
