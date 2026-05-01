using System.Security.Cryptography;
using Arbor.DevPackages.Core.Packages;

namespace Arbor.DevPackages.Testing;

/// <summary>
/// In-memory fake implementation of <see cref="IPackageStore"/> for use in tests.
/// </summary>
public sealed class InMemoryPackageStore : IPackageStore
{
    private sealed record StoredPackage(byte[] Nupkg, string Nuspec, string Sha512Hash);

    private readonly Dictionary<PackageIdentity, StoredPackage> _packages =
        new(PackageIdentityComparer.Instance);

    /// <summary>
    /// Adds a package to the in-memory store so tests can exercise known-package scenarios.
    /// </summary>
    public void Add(PackageIdentity identity, byte[] nupkg, string nuspec)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(nupkg);
        ArgumentNullException.ThrowIfNull(nuspec);

        // Copy the array so caller mutations cannot corrupt the stored content.
        byte[] stored = (byte[])nupkg.Clone();
        string sha512 = ComputeSha512(stored);
        var key = new PackageIdentity(
            identity.Id.ToLowerInvariant(),
            identity.Version.ToLowerInvariant());
        _packages[key] = new StoredPackage(stored, nuspec, sha512);
    }

    public Task<PackageMetadata?> GetMetadataAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        var key = Normalise(identity);
        if (_packages.TryGetValue(key, out var pkg))
        {
            return Task.FromResult<PackageMetadata?>(
                new PackageMetadata(key, pkg.Sha512Hash, pkg.Nuspec));
        }

        return Task.FromResult<PackageMetadata?>(null);
    }

    public Task<string?> GetStoredHashAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        var key = Normalise(identity);
        if (_packages.TryGetValue(key, out var pkg))
        {
            return Task.FromResult<string?>(pkg.Sha512Hash);
        }

        return Task.FromResult<string?>(null);
    }

    public Task<Stream?> OpenNupkgAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        var key = Normalise(identity);
        if (_packages.TryGetValue(key, out var pkg))
        {
            // Return a read-only stream so callers cannot mutate the stored bytes.
            return Task.FromResult<Stream?>(new MemoryStream(pkg.Nupkg, writable: false));
        }

        return Task.FromResult<Stream?>(null);
    }

    public Task<Stream?> OpenNuspecAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        var key = Normalise(identity);
        if (_packages.TryGetValue(key, out var pkg))
        {
            return Task.FromResult<Stream?>(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pkg.Nuspec)));
        }

        return Task.FromResult<Stream?>(null);
    }

    public Task<PackageStoreResult> StoreAsync(
        PackageIdentity identity,
        Stream nupkg,
        Stream nuspec,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("InMemoryPackageStore does not support StoreAsync; use Add() instead.");

    public Task<bool> ExistsAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        var key = Normalise(identity);
        return Task.FromResult(_packages.ContainsKey(key));
    }

    public Task DeleteAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        var key = Normalise(identity);
        _packages.Remove(key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PackageIdentity>> ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PackageIdentity>>([.. _packages.Keys]);

    private static PackageIdentity Normalise(PackageIdentity identity) =>
        new(identity.Id.ToLowerInvariant(), identity.Version.ToLowerInvariant());

    private static string ComputeSha512(byte[] data)
    {
        byte[] hash = SHA512.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private sealed class PackageIdentityComparer : IEqualityComparer<PackageIdentity>
    {
        public static readonly PackageIdentityComparer Instance = new();

        public bool Equals(PackageIdentity? x, PackageIdentity? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return string.Equals(x.Id, y.Id, StringComparison.Ordinal) &&
                   string.Equals(x.Version, y.Version, StringComparison.Ordinal);
        }

        public int GetHashCode(PackageIdentity obj) =>
            HashCode.Combine(obj.Id, obj.Version);
    }
}
