namespace Arbor.DevPackages.Core.Packages;

public interface IPackageStore
{
    Task<PackageMetadata?> GetMetadataAsync(PackageIdentity identity, CancellationToken cancellationToken);
    Task<Stream?> OpenNupkgAsync(PackageIdentity identity, CancellationToken cancellationToken);
    Task<Stream?> OpenNuspecAsync(PackageIdentity identity, CancellationToken cancellationToken);
    Task StoreAsync(PackageIdentity identity, Stream nupkg, Stream nuspec, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(PackageIdentity identity, CancellationToken cancellationToken);
}
