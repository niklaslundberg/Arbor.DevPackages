namespace Arbor.DevPackages.Core.Retention;

public sealed class RetentionOptions
{
    private TimeSpan _retentionWindow = TimeSpan.FromDays(30);
    private TimeSpan _schedulerInterval = TimeSpan.FromHours(1);
    private TimeSpan _inactivityThreshold = TimeSpan.FromMinutes(5);

    public static readonly RetentionOptions Default = new();

    /// <summary>
    /// Packages not downloaded within this window are eligible for purging.
    /// Default: 30 days.
    /// </summary>
    public TimeSpan RetentionWindow
    {
        get => _retentionWindow;
        init => _retentionWindow = ValidatePositive(value, nameof(RetentionWindow));
    }

    /// <summary>
    /// How often the retention scheduler runs.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan SchedulerInterval
    {
        get => _schedulerInterval;
        init => _schedulerInterval = ValidatePositive(value, nameof(SchedulerInterval));
    }

    /// <summary>
    /// Minimum time since the last download across all packages before a purge run is allowed.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan InactivityThreshold
    {
        get => _inactivityThreshold;
        init => _inactivityThreshold = ValidatePositive(value, nameof(InactivityThreshold));
    }

    private static TimeSpan ValidatePositive(TimeSpan value, string propertyName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(propertyName, value,
                "The value must be greater than TimeSpan.Zero.");
        }

        return value;
    }
}
