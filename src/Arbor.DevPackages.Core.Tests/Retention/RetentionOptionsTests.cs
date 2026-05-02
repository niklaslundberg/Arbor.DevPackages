using Arbor.DevPackages.Core.Retention;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests.Retention;

public sealed class RetentionOptionsTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var options = RetentionOptions.Default;

        options.RetentionWindow.Should().Be(TimeSpan.FromDays(30));
        options.SchedulerInterval.Should().Be(TimeSpan.FromHours(1));
        options.InactivityThreshold.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void RetentionWindow_WhenSetToPositiveValue_IsAccepted()
    {
        var options = new RetentionOptions { RetentionWindow = TimeSpan.FromDays(7) };

        options.RetentionWindow.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void RetentionWindow_WhenSetToZero_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new RetentionOptions { RetentionWindow = TimeSpan.Zero };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RetentionWindow_WhenSetToNegative_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new RetentionOptions { RetentionWindow = TimeSpan.FromDays(-1) };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SchedulerInterval_WhenSetToPositiveValue_IsAccepted()
    {
        var options = new RetentionOptions { SchedulerInterval = TimeSpan.FromMinutes(30) };

        options.SchedulerInterval.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void SchedulerInterval_WhenSetToZero_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new RetentionOptions { SchedulerInterval = TimeSpan.Zero };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SchedulerInterval_WhenSetToNegative_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new RetentionOptions { SchedulerInterval = TimeSpan.FromHours(-1) };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InactivityThreshold_WhenSetToPositiveValue_IsAccepted()
    {
        var options = new RetentionOptions { InactivityThreshold = TimeSpan.FromMinutes(10) };

        options.InactivityThreshold.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void InactivityThreshold_WhenSetToZero_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new RetentionOptions { InactivityThreshold = TimeSpan.Zero };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InactivityThreshold_WhenSetToNegative_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new RetentionOptions { InactivityThreshold = TimeSpan.FromMinutes(-5) };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
