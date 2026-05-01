namespace Arbor.DevPackages.Core.Packages;

public sealed class PackageIntegrityException : Exception
{
    public PackageIntegrityException(PackageIdentity identity)
        : base($"SHA-512 integrity check failed for package {identity.Id} {identity.Version}.")
    {
    }
}
