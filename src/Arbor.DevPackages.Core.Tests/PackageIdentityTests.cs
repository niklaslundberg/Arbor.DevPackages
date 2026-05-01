using Arbor.DevPackages.Core.Packages;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests;

public class PackageIdentityTests
{
    [Fact]
    public void PackageIdentity_WithSameIdAndVersion_AreEqual()
    {
        var a = new PackageIdentity("Serilog", "3.1.1");
        var b = new PackageIdentity("Serilog", "3.1.1");

        a.Should().Be(b);
    }

    [Fact]
    public void PackageIdentity_WithDifferentVersion_AreNotEqual()
    {
        var a = new PackageIdentity("Serilog", "3.1.1");
        var b = new PackageIdentity("Serilog", "3.2.0");

        a.Should().NotBe(b);
    }
}
