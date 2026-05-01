using NuGet.Versioning;

namespace Arbor.DevPackages.Server;

/// <summary>
/// Compares NuGet version strings using semantic versioning order.
/// Falls back to ordinal string comparison when either value is not a valid semver.
/// </summary>
internal sealed class SemVerComparer : IComparer<string>
{
    public static readonly SemVerComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (NuGetVersion.TryParse(x, out var vx) && NuGetVersion.TryParse(y, out var vy))
        {
            return VersionComparer.Default.Compare(vx, vy);
        }

        return StringComparer.Ordinal.Compare(x, y);
    }
}
