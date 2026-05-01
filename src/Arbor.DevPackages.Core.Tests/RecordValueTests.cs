using Arbor.DevPackages.Core.Packages;
using Arbor.DevPackages.Core.Retention;
using Arbor.DevPackages.Core.Statistics;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests;

public class RecordValueTests
{
    [Fact]
    public void PackageMetadata_WithValues_ExposesProperties()
    {
        var identity = new PackageIdentity("Serilog", "3.1.1");
        var metadata = new PackageMetadata(identity, "abc123hash", "<package/>");

        metadata.Identity.Should().Be(identity);
        metadata.Sha512Hash.Should().Be("abc123hash");
        metadata.NuspecContent.Should().Be("<package/>");
    }

    [Fact]
    public void DownloadEvent_WithValues_ExposesProperties()
    {
        var identity = new PackageIdentity("Serilog", "3.1.1");
        var at = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var evt = new DownloadEvent(identity, at);

        evt.Identity.Should().Be(identity);
        evt.DownloadedAt.Should().Be(at);
    }

    [Fact]
    public void RetentionDecision_KeepAction_ExposesProperties()
    {
        var decision = new RetentionDecision(RetentionAction.Keep, "package is recent");

        decision.Action.Should().Be(RetentionAction.Keep);
        decision.Reason.Should().Be("package is recent");
    }

    [Fact]
    public void RetentionDecision_PurgeAction_ExposesProperties()
    {
        var decision = new RetentionDecision(RetentionAction.Purge, "package is stale");

        decision.Action.Should().Be(RetentionAction.Purge);
        decision.Reason.Should().Be("package is stale");
    }
}
