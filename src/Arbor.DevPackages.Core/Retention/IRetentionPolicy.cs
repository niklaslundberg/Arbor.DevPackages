using Arbor.DevPackages.Core.Packages;

namespace Arbor.DevPackages.Core.Retention;

public interface IRetentionPolicy
{
    Task<RetentionDecision> EvaluateAsync(PackageIdentity identity, CancellationToken cancellationToken);
}
