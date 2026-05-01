using System.Security.Cryptography;
using System.Text;
using Arbor.DevPackages.Core.Packages;
using AwesomeAssertions;
using Xunit;

namespace Arbor.DevPackages.Core.Tests;

public sealed class FileSystemPackageStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemPackageStore _store;
    private readonly PackageIdentity _identity = new("Serilog", "3.1.1");

    public FileSystemPackageStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _store = new FileSystemPackageStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static MemoryStream MakeStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task Store_NewPackage_StoresNupkgAndNuspecAndHash()
    {
        using MemoryStream nupkg = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec = MakeStream("<package />");

        PackageStoreResult result = await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        result.Should().Be(PackageStoreResult.Stored);

        string id = _identity.Id.ToLowerInvariant();
        string version = _identity.Version.ToLowerInvariant();
        string dir = Path.Combine(_tempDir, id, version);

        File.Exists(Path.Combine(dir, $"{id}.{version}.nupkg")).Should().BeTrue();
        File.Exists(Path.Combine(dir, $"{id}.{version}.nuspec")).Should().BeTrue();

        string sha512File = Path.Combine(dir, $"{id}.{version}.sha512");
        File.Exists(sha512File).Should().BeTrue();

        string storedHash = await File.ReadAllTextAsync(sha512File);
        storedHash.Should().NotBeEmpty();

        byte[] nupkgBytes = Encoding.UTF8.GetBytes("fake-nupkg-content");
        string expectedHash = Convert.ToHexStringLower(SHA512.HashData(nupkgBytes));
        storedHash.Should().Be(expectedHash);
    }

    [Fact]
    public async Task Store_SamePackageTwice_ReturnsAlreadyExists()
    {
        using MemoryStream nupkg1 = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec1 = MakeStream("<package />");
        await _store.StoreAsync(_identity, nupkg1, nuspec1, CancellationToken.None);

        using MemoryStream nupkg2 = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec2 = MakeStream("<package />");
        PackageStoreResult result = await _store.StoreAsync(_identity, nupkg2, nuspec2, CancellationToken.None);

        result.Should().Be(PackageStoreResult.AlreadyExists);
    }

    [Fact]
    public async Task Read_StoredPackage_ReturnsSameBytes()
    {
        byte[] originalBytes = Encoding.UTF8.GetBytes("fake-nupkg-content");
        using MemoryStream nupkg = new(originalBytes);
        using MemoryStream nuspec = MakeStream("<package />");
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        Stream? result = await _store.OpenNupkgAsync(_identity, CancellationToken.None);

        result.Should().NotBeNull();
        using MemoryStream ms = new();
        await result!.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(originalBytes);
    }

    [Fact]
    public async Task Read_MissingPackage_ReturnsNull()
    {
        Stream? result = await _store.OpenNupkgAsync(_identity, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Read_TamperedPackage_ThrowsPackageIntegrityException()
    {
        using MemoryStream nupkg = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec = MakeStream("<package />");
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        string id = _identity.Id.ToLowerInvariant();
        string version = _identity.Version.ToLowerInvariant();
        string nupkgPath = Path.Combine(_tempDir, id, version, $"{id}.{version}.nupkg");
        await File.WriteAllTextAsync(nupkgPath, "tampered-content");

        Func<Task> act = () => _store.OpenNupkgAsync(_identity, CancellationToken.None);

        await act.Should().ThrowAsync<PackageIntegrityException>();
    }

    [Fact]
    public async Task Read_MissingSidecar_ThrowsPackageIntegrityException()
    {
        using MemoryStream nupkg = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec = MakeStream("<package />");
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        string id = _identity.Id.ToLowerInvariant();
        string version = _identity.Version.ToLowerInvariant();
        string sha512Path = Path.Combine(_tempDir, id, version, $"{id}.{version}.sha512");
        File.Delete(sha512Path);

        Func<Task> act = () => _store.OpenNupkgAsync(_identity, CancellationToken.None);

        await act.Should().ThrowAsync<PackageIntegrityException>();
    }
}
