using System.Security.Cryptography;

namespace Arbor.DevPackages.Core.Packages;

public sealed class FileSystemPackageStore : IPackageStore
{
    private readonly string _storePath;

    public FileSystemPackageStore(string storePath)
    {
        ArgumentNullException.ThrowIfNull(storePath);
        _storePath = storePath;
    }

    private static string PackageDir(string storePath, PackageIdentity identity) =>
        Path.Combine(storePath, identity.Id.ToLowerInvariant(), identity.Version.ToLowerInvariant());

    private static string NupkgPath(string storePath, PackageIdentity identity)
    {
        string dir = PackageDir(storePath, identity);
        string id = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToLowerInvariant();
        return Path.Combine(dir, $"{id}.{version}.nupkg");
    }

    private static string NuspecPath(string storePath, PackageIdentity identity)
    {
        string dir = PackageDir(storePath, identity);
        string id = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToLowerInvariant();
        return Path.Combine(dir, $"{id}.{version}.nuspec");
    }

    private static string Sha512Path(string storePath, PackageIdentity identity)
    {
        string dir = PackageDir(storePath, identity);
        string id = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToLowerInvariant();
        return Path.Combine(dir, $"{id}.{version}.sha512");
    }

    private static async Task<string> ComputeSha512Async(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        byte[] hashBytes = await SHA512.HashDataAsync(stream, cancellationToken);
        stream.Position = 0;
        return Convert.ToHexStringLower(hashBytes);
    }

    private static async Task<string> ComputeSha512FromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        await using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hashBytes = await SHA512.HashDataAsync(fs, cancellationToken);
        return Convert.ToHexStringLower(hashBytes);
    }

    public async Task<PackageStoreResult> StoreAsync(
        PackageIdentity identity,
        Stream nupkg,
        Stream nuspec,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(nupkg);
        ArgumentNullException.ThrowIfNull(nuspec);

        string nupkgPath = NupkgPath(_storePath, identity);

        if (File.Exists(nupkgPath))
        {
            return PackageStoreResult.AlreadyExists;
        }

        string dir = PackageDir(_storePath, identity);
        Directory.CreateDirectory(dir);

        string sha512 = await ComputeSha512Async(nupkg, cancellationToken);

        nupkg.Position = 0;
        await using (FileStream dest = new(nupkgPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await nupkg.CopyToAsync(dest, cancellationToken);
        }

        string nuspecPath = NuspecPath(_storePath, identity);
        nuspec.Position = 0;
        await using (FileStream dest = new(nuspecPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await nuspec.CopyToAsync(dest, cancellationToken);
        }

        string sha512Path = Sha512Path(_storePath, identity);
        await File.WriteAllTextAsync(sha512Path, sha512, cancellationToken);

        return PackageStoreResult.Stored;
    }

    public async Task<Stream?> OpenNupkgAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        string nupkgPath = NupkgPath(_storePath, identity);
        if (!File.Exists(nupkgPath))
        {
            return null;
        }

        string sha512Path = Sha512Path(_storePath, identity);
        if (!File.Exists(sha512Path))
        {
            throw new PackageIntegrityException(identity);
        }

        string storedHash = await File.ReadAllTextAsync(sha512Path, cancellationToken);
        string actualHash = await ComputeSha512FromFileAsync(nupkgPath, cancellationToken);

        if (!string.Equals(storedHash, actualHash, StringComparison.Ordinal))
        {
            throw new PackageIntegrityException(identity);
        }

        return new FileStream(nupkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public Task<Stream?> OpenNuspecAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        string nuspecPath = NuspecPath(_storePath, identity);
        if (!File.Exists(nuspecPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(nuspecPath, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public async Task<PackageMetadata?> GetMetadataAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        string nupkgPath = NupkgPath(_storePath, identity);
        if (!File.Exists(nupkgPath))
        {
            return null;
        }

        string sha512Path = Sha512Path(_storePath, identity);
        if (!File.Exists(sha512Path))
        {
            throw new PackageIntegrityException(identity);
        }

        string sha512 = await File.ReadAllTextAsync(sha512Path, cancellationToken);

        string nuspecPath = NuspecPath(_storePath, identity);
        if (!File.Exists(nuspecPath))
        {
            throw new PackageIntegrityException(identity);
        }

        string nuspecContent = await File.ReadAllTextAsync(nuspecPath, cancellationToken);

        return new PackageMetadata(identity, sha512, nuspecContent);
    }

    public Task<bool> ExistsAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Task.FromResult(File.Exists(NupkgPath(_storePath, identity)));
    }

    public Task DeleteAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        string dir = PackageDir(_storePath, identity);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        return Task.CompletedTask;
    }
}
