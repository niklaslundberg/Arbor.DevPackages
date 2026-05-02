namespace Arbor.DevPackages.Core.Packages;

public sealed class PackageIntegrityException : Exception
{
    public PackageIntegrityException()
    {
    }

    public PackageIntegrityException(string message) : base(message)
    {
    }

    public PackageIntegrityException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public PackageIntegrityException(PackageIdentity identity)
        : base($"SHA-512 integrity check failed for package {identity.Id} {identity.Version}.")
    {
    }
}
