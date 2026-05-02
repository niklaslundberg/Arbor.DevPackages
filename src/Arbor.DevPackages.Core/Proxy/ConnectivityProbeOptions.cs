namespace Arbor.DevPackages.Core.Proxy;

/// <summary>
/// Configuration options for <see cref="PassiveConnectivityProbe"/>.
/// </summary>
public sealed class ConnectivityProbeOptions
{
    private TimeSpan _backoffDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets the default options (60-second back-off).
    /// </summary>
    public static ConnectivityProbeOptions Default => new();

    /// <summary>
    /// How long to treat an upstream as offline after the first fetch failure.
    /// Defaults to 60 seconds. Must be positive.
    /// </summary>
    public TimeSpan BackoffDuration
    {
        get => _backoffDuration;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "BackoffDuration must be positive.");
            }

            _backoffDuration = value;
        }
    }
}
