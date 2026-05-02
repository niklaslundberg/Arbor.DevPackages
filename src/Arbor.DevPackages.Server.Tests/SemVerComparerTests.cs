using Arbor.DevPackages.Server;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Server.Tests;

public sealed class SemVerComparerTests
{
    [Fact]
    public void Compare_OlderVersionFirst_ReturnsNegative()
    {
        int result = SemVerComparer.Instance.Compare("1.0.0", "2.0.0");

        result.Should().BeLessThan(0);
    }

    [Fact]
    public void Compare_NewerVersionFirst_ReturnsPositive()
    {
        int result = SemVerComparer.Instance.Compare("2.0.0", "1.0.0");

        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Compare_SameVersion_ReturnsZero()
    {
        int result = SemVerComparer.Instance.Compare("3.1.1", "3.1.1");

        result.Should().Be(0);
    }

    [Fact]
    public void Compare_PrereleaseIsLessThanStable()
    {
        int result = SemVerComparer.Instance.Compare("1.0.0-beta", "1.0.0");

        result.Should().BeLessThan(0);
    }

    [Fact]
    public void Compare_InvalidSemVer_FallsBackToOrdinalStringComparison()
    {
        // "abc" and "xyz" are not valid semver strings; ordinal 'a' < 'x'.
        int result = SemVerComparer.Instance.Compare("abc", "xyz");

        result.Should().BeLessThan(0);
    }

    [Fact]
    public void Compare_NullValues_FallsBackToOrdinalStringComparison()
    {
        // StringComparer.Ordinal.Compare(null, "1.0.0") is negative (null sorts before any string).
        int result = SemVerComparer.Instance.Compare(null, "1.0.0");

        result.Should().BeLessThan(0);
    }
}
