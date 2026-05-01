using System.Security.Cryptography;

namespace Arbor.DevPackages.Core.Packages;

public sealed class FileSystemPackageStore : IPackageStore
{
    private readonly string _storePath;

    public FileSystemPackageStore(string storePath)
    {
        ArgumentNullException.ThrowIfNull(storePath);
        _storePath = Path.GetFullPath(storePath);
    }

    // --- Path helpers ---

    private static void ValidateIdentitySegment(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Package {paramName} must not be null or whitespace.", paramName);
        }

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains("..") ||
            Path.IsPathRooted(value))
        {
            throw new ArgumentException($"Package {paramName} '{value}' contains invalid path characters.", paramName);
        }
    }

    private static void ValidateIdentity(PackageIdentity identity)
    {
        ValidateIdentitySegment(identity.Id, nameof(identity.Id));
        ValidateIdentitySegment(identity.Version, nameof(identity.Version));
    }

    private string PackageDir(PackageIdentity identity)
    {
        string dir = Path.GetFullPath(
            Path.Combine(_storePath, identity.Id.ToLowerInvariant(), identity.Version.ToLowerInvariant()));

        if (!dir.StartsWith(_storePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Package identity would escape the store path.");
        }

        return dir;
    }

    private string NupkgPath(PackageIdentity identity)
    {
        string dir = PackageDir(identity);
        string id = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToLowerInvariant();
        return Path.Combine(dir, $"{id}.{version}.nupkg");
    }

    private string NuspecPath(PackageIdentity identity)
    {
        string dir = PackageDir(identity);
        string id = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToLowerInvariant();
        return Path.Combine(dir, $"{id}.{version}.nuspec");
    }

    private string Sha512Path(PackageIdentity identity)
    {
        string dir = PackageDir(identity);
        string id = identity.Id.ToLowerInvariant();
        string version = identity.Version.ToLowerInvariant();
        return Path.Combine(dir, $"{id}.{version}.sha512");
    }

    // --- Hashing helpers ---

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/> in a single pass,
    /// computing the SHA-512 hash along the way. Works with non-seekable streams.
    /// </summary>
    private static async Task<string> CopyAndHashAsync(
        Stream source, Stream destination, CancellationToken cancellationToken)
    {
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    private static async Task<string> ComputeSha512FromFileAsync(
        string filePath, CancellationToken cancellationToken)
    {
        await using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hashBytes = await SHA512.HashDataAsync(fs, cancellationToken);
        return Convert.ToHexStringLower(hashBytes);
    }

    // --- Cleanup helper ---

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup — ignore failures.
        }
    }

    // --- IPackageStore ---

    public async Task<PackageStoreResult> StoreAsync(
        PackageIdentity identity,
        Stream nupkg,
        Stream nuspec,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(nupkg);
        ArgumentNullException.ThrowIfNull(nuspec);
        ValidateIdentity(identity);

        string nupkgPath = NupkgPath(identity);
        string nuspecPath = NuspecPath(identity);
        string sha512Path = Sha512Path(identity);

        string dir = PackageDir(identity);
        Directory.CreateDirectory(dir);

        string nupkgTmp = nupkgPath + ".tmp";
        string nuspecTmp = nuspecPath + ".tmp";
        string sha512Tmp = sha512Path + ".tmp";

        try
        {
            // Single-pass copy+hash — works with non-seekable (e.g., network) streams.
            string sha512;
            await using (FileStream dest = new(nupkgTmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                sha512 = await CopyAndHashAsync(nupkg, dest, cancellationToken);
            }

            await using (FileStream dest = new(nuspecTmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await nuspec.CopyToAsync(dest, cancellationToken);
            }

            await File.WriteAllTextAsync(sha512Tmp, sha512, cancellationToken);

            // Atomic promotion: move nupkg temp into place (no overwrite = idempotency guard).
            try
            {
                File.Move(nupkgTmp, nupkgPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(nupkgPath))
            {
                // Another writer beat us — surface as AlreadyExists.
                return PackageStoreResult.AlreadyExists;
            }

            File.Move(nuspecTmp, nuspecPath, overwrite: true);
            File.Move(sha512Tmp, sha512Path, overwrite: true);

            return PackageStoreResult.Stored;
        }
        catch
        {
            TryDeleteFile(nupkgTmp);
            TryDeleteFile(nuspecTmp);
            TryDeleteFile(sha512Tmp);
            throw;
        }
    }

    public async Task<Stream?> OpenNupkgAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateIdentity(identity);

        string nupkgPath = NupkgPath(identity);
        if (!File.Exists(nupkgPath))
        {
            return null;
        }

        string sha512Path = Sha512Path(identity);
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
        ValidateIdentity(identity);

        // Use .nupkg presence as the existence signal.
        string nupkgPath = NupkgPath(identity);
        if (!File.Exists(nupkgPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        string nuspecPath = NuspecPath(identity);
        if (!File.Exists(nuspecPath))
        {
            return Task.FromException<Stream?>(new PackageIntegrityException(identity));
        }

        return Task.FromResult<Stream?>(
            new FileStream(nuspecPath, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public async Task<PackageMetadata?> GetMetadataAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateIdentity(identity);

        string nupkgPath = NupkgPath(identity);
        if (!File.Exists(nupkgPath))
        {
            return null;
        }

        string sha512Path = Sha512Path(identity);
        if (!File.Exists(sha512Path))
        {
            throw new PackageIntegrityException(identity);
        }

        // Verify integrity before serving metadata.
        string storedHash = await File.ReadAllTextAsync(sha512Path, cancellationToken);
        string actualHash = await ComputeSha512FromFileAsync(nupkgPath, cancellationToken);

        if (!string.Equals(storedHash, actualHash, StringComparison.Ordinal))
        {
            throw new PackageIntegrityException(identity);
        }

        string nuspecPath = NuspecPath(identity);
        if (!File.Exists(nuspecPath))
        {
            throw new PackageIntegrityException(identity);
        }

        string nuspecContent = await File.ReadAllTextAsync(nuspecPath, cancellationToken);

        return new PackageMetadata(identity, storedHash, nuspecContent);
    }

    public async Task<string?> GetStoredHashAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateIdentity(identity);

        string nupkgPath = NupkgPath(identity);
        if (!File.Exists(nupkgPath))
        {
            return null;
        }

        string sha512Path = Sha512Path(identity);
        if (!File.Exists(sha512Path))
        {
            throw new PackageIntegrityException(identity);
        }

        return await File.ReadAllTextAsync(sha512Path, cancellationToken);
    }

    public Task<bool> ExistsAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateIdentity(identity);
        return Task.FromResult(File.Exists(NupkgPath(identity)));
    }

    public Task DeleteAsync(PackageIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateIdentity(identity);

        string dir = PackageDir(identity);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PackageIdentity>> ListAllAsync(CancellationToken cancellationToken)
    {
        var results = new List<PackageIdentity>();

        if (!Directory.Exists(_storePath))
        {
            return Task.FromResult<IReadOnlyList<PackageIdentity>>(results);
        }

        foreach (string idDir in Directory.EnumerateDirectories(_storePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string packageId = Path.GetFileName(idDir);
            foreach (string versionDir in Directory.EnumerateDirectories(idDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string version = Path.GetFileName(versionDir);
                string nupkgPath = Path.Combine(versionDir, $"{packageId}.{version}.nupkg");
                if (File.Exists(nupkgPath))
                {
                    results.Add(new PackageIdentity(packageId, version));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<PackageIdentity>>(results);
    }
}
