namespace Arbor.DevPackages.Core.Packages;

public sealed record PackageMetadata(PackageIdentity Identity, string Sha512Hash, string NuspecContent);
