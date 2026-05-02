using System.Diagnostics;

namespace Arbor.DevPackages.SystemTests;

/// <summary>
/// Runs <c>dotnet nuget push</c> against the test server as a subprocess.
/// Each call pushes the provided package file path directly; no temporary directory is created.
/// </summary>
public sealed class NuGetPushRunner
{
    /// <summary>Result of a <c>dotnet nuget push</c> run.</summary>
    public sealed record PushResult(int ExitCode, string Output, string Error);

    /// <summary>
    /// Pushes the given <paramref name="nupkgPath"/> to the test server at
    /// <paramref name="serverPort"/> on feed <paramref name="feedId"/>.
    /// </summary>
    public static async Task<PushResult> RunAsync(
        int serverPort,
        string nupkgPath,
        string feedId = "nuget-org",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);
        if (!File.Exists(nupkgPath))
        {
            throw new FileNotFoundException($"nupkg not found: '{nupkgPath}'", nupkgPath);
        }

        var sourceUrl = $"http://localhost:{serverPort}/feeds/{feedId}/v3/index.json";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("push");
        psi.ArgumentList.Add(nupkgPath);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(sourceUrl);
        psi.ArgumentList.Add("--api-key");
        psi.ArgumentList.Add("any");
        psi.ArgumentList.Add("--allow-insecure-connections");

        // Inherit parent environment.
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            psi.Environment[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'dotnet nuget push' process.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        return new PushResult(process.ExitCode, output, error);
    }
}
