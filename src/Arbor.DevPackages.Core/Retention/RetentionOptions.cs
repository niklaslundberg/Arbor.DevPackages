namespace Arbor.DevPackages.Core.Retention;

public sealed class RetentionOptions
{
    public static readonly RetentionOptions Default = new();

    /// <summary>
    /// Packages not downloaded within this window are eligible for purging.
    /// Default: 30 days.
    /// </summary>
    public TimeSpan RetentionWindow { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How often the retention scheduler runs.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan SchedulerInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Minimum time since the last download across all packages before a purge run is allowed.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan InactivityThreshold { get; init; } = TimeSpan.FromMinutes(5);
}
