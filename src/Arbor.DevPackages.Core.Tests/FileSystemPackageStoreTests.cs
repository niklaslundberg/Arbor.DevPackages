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

    // ── Store ────────────────────────────────────────────────────────────────

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
    public async Task Store_WithNonSeekableStream_StoresSuccessfully()
    {
        byte[] originalBytes = Encoding.UTF8.GetBytes("non-seekable-content");
        using MemoryStream inner = new(originalBytes);
        using NonSeekableStream nupkg = new(inner);
        using MemoryStream nuspec = MakeStream("<package />");

        PackageStoreResult result = await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        result.Should().Be(PackageStoreResult.Stored);

        string id = _identity.Id.ToLowerInvariant();
        string version = _identity.Version.ToLowerInvariant();
        byte[] stored = await File.ReadAllBytesAsync(
            Path.Combine(_tempDir, id, version, $"{id}.{version}.nupkg"));
        stored.Should().BeEquivalentTo(originalBytes);
    }

    [Fact]
    public async Task Store_WithPathTraversalId_ThrowsArgumentException()
    {
        var maliciousIdentity = new PackageIdentity("../evil", "1.0.0");
        using MemoryStream nupkg = MakeStream("content");
        using MemoryStream nuspec = MakeStream("<package />");

        Func<Task> act = () => _store.StoreAsync(maliciousIdentity, nupkg, nuspec, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── OpenNupkg ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Read_StoredPackage_ReturnsSameBytes()
    {
        byte[] originalBytes = Encoding.UTF8.GetBytes("fake-nupkg-content");
        using MemoryStream nupkg = new(originalBytes);
        using MemoryStream nuspec = MakeStream("<package />");
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        await using Stream? result = await _store.OpenNupkgAsync(_identity, CancellationToken.None);

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

    // ── OpenNuspec ───────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenNuspecAsync_StoredPackage_ReturnsNuspecContent()
    {
        const string nuspecXml = "<package><metadata><id>Serilog</id></metadata></package>";
        using MemoryStream nupkg = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec = MakeStream(nuspecXml);
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        await using Stream? result = await _store.OpenNuspecAsync(_identity, CancellationToken.None);

        result.Should().NotBeNull();
        using StreamReader reader = new(result!);
        string content = await reader.ReadToEndAsync();
        content.Should().Be(nuspecXml);
    }

    [Fact]
    public async Task OpenNuspecAsync_MissingPackage_ReturnsNull()
    {
        Stream? result = await _store.OpenNuspecAsync(_identity, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task OpenNuspecAsync_MissingNuspecFile_ThrowsPackageIntegrityException()
    {
        using MemoryStream nupkg = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec = MakeStream("<package />");
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        string id = _identity.Id.ToLowerInvariant();
        string version = _identity.Version.ToLowerInvariant();
        string nuspecPath = Path.Combine(_tempDir, id, version, $"{id}.{version}.nuspec");
        File.Delete(nuspecPath);

        Func<Task> act = () => _store.OpenNuspecAsync(_identity, CancellationToken.None);

        await act.Should().ThrowAsync<PackageIntegrityException>();
    }

    // ── GetMetadata ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetadataAsync_StoredPackage_ReturnsMetadata()
    {
        const string nuspecXml = "<package />";
        byte[] nupkgBytes = Encoding.UTF8.GetBytes("fake-nupkg-content");
        using MemoryStream nupkg = new(nupkgBytes);
        using MemoryStream nuspec = MakeStream(nuspecXml);
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        PackageMetadata? metadata = await _store.GetMetadataAsync(_identity, CancellationToken.None);

        metadata.Should().NotBeNull();
        metadata!.Identity.Should().Be(_identity);
        metadata.NuspecContent.Should().Be(nuspecXml);
        metadata.Sha512Hash.Should().Be(Convert.ToHexStringLower(SHA512.HashData(nupkgBytes)));
    }

    [Fact]
    public async Task GetMetadataAsync_MissingPackage_ReturnsNull()
    {
        PackageMetadata? result = await _store.GetMetadataAsync(_identity, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMetadataAsync_TamperedPackage_ThrowsPackageIntegrityException()
    {
        using MemoryStream nupkg = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec = MakeStream("<package />");
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        string id = _identity.Id.ToLowerInvariant();
        string version = _identity.Version.ToLowerInvariant();
        string nupkgPath = Path.Combine(_tempDir, id, version, $"{id}.{version}.nupkg");
        await File.WriteAllTextAsync(nupkgPath, "tampered");

        Func<Task> act = () => _store.GetMetadataAsync(_identity, CancellationToken.None);

        await act.Should().ThrowAsync<PackageIntegrityException>();
    }

    // ── ExistsAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_StoredPackage_ReturnsTrue()
    {
        using MemoryStream nupkg = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec = MakeStream("<package />");
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        bool exists = await _store.ExistsAsync(_identity, CancellationToken.None);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_MissingPackage_ReturnsFalse()
    {
        bool exists = await _store.ExistsAsync(_identity, CancellationToken.None);

        exists.Should().BeFalse();
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingPackage_RemovesAllFiles()
    {
        using MemoryStream nupkg = MakeStream("fake-nupkg-content");
        using MemoryStream nuspec = MakeStream("<package />");
        await _store.StoreAsync(_identity, nupkg, nuspec, CancellationToken.None);

        await _store.DeleteAsync(_identity, CancellationToken.None);

        bool exists = await _store.ExistsAsync(_identity, CancellationToken.None);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonExistingPackage_DoesNotThrow()
    {
        Func<Task> act = () => _store.DeleteAsync(_identity, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Wraps a stream and reports <c>CanSeek = false</c>.</summary>
    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            _inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            _inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
