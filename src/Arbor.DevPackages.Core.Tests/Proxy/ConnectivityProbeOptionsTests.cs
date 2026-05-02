using Arbor.DevPackages.Core.Proxy;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests.Proxy;

public sealed class ConnectivityProbeOptionsTests
{
    [Fact]
    public void Default_HasExpectedBackoffDuration()
    {
        var options = ConnectivityProbeOptions.Default;

        options.BackoffDuration.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void BackoffDuration_WhenSetToPositiveValue_IsAccepted()
    {
        var options = new ConnectivityProbeOptions { BackoffDuration = TimeSpan.FromMinutes(2) };

        options.BackoffDuration.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void BackoffDuration_WhenSetToZero_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new ConnectivityProbeOptions { BackoffDuration = TimeSpan.Zero };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BackoffDuration_WhenSetToNegative_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new ConnectivityProbeOptions { BackoffDuration = TimeSpan.FromSeconds(-30) };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
