using Arbor.DevPackages.Core.Packages;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests.Packages;

public sealed class PackageIntegrityExceptionTests
{
    [Fact]
    public void Constructor_Default_CanBeInstantiated()
    {
        var ex = new PackageIntegrityException();

        ex.Should().NotBeNull();
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        const string message = "integrity check failed";

        var ex = new PackageIntegrityException(message);

        ex.Message.Should().Be(message);
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBoth()
    {
        const string message = "outer message";
        var inner = new InvalidOperationException("inner");

        var ex = new PackageIntegrityException(message, inner);

        ex.Message.Should().Be(message);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Constructor_WithIdentity_ContainsIdAndVersionInMessage()
    {
        var identity = new PackageIdentity("Serilog", "3.1.1");

        var ex = new PackageIntegrityException(identity);

        ex.Message.Should().Contain("Serilog");
        ex.Message.Should().Contain("3.1.1");
    }
}
