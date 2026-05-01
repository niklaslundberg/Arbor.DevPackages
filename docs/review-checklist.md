# PR Review Checklist

Apply these checks before every PR is marked ready for review.

## CodeQL / Static Analysis

### Dispose IDisposable locals

Wrap every locally created `IDisposable` in a `using` declaration or statement.

```csharp
// ✗ Triggers CodeQL: CA2000 / Missing Dispose call on local IDisposable
var response = new HttpResponseMessage();

// ✓ Correct
using var response = new HttpResponseMessage();
```

### Use `.Where()` instead of implicit filtering in `foreach`

```csharp
// ✗ Triggers CodeQL
foreach (var item in collection)
{
    if (item.IsActive) { Process(item); }
}

// ✓ Correct
foreach (var item in collection.Where(x => x.IsActive))
{
    Process(item);
}
```

### Mark fields `readonly` when not mutated after construction

```csharp
// ✗ Triggers CodeQL
private SomeService _service;

// ✓ Correct
private readonly SomeService _service;
```

### Avoid unnecessary async state machine when delegating

When a method does nothing but forward to one other async call, return the `Task`/`ValueTask` directly instead of using `async`/`await`, which allocates an unnecessary state machine.

```csharp
// ✗ Allocates a state machine unnecessarily
public async ValueTask DisposeAsync()
{
    await _connection.DisposeAsync();
}

// ✓ Return the ValueTask directly
public ValueTask DisposeAsync() => _connection.DisposeAsync();
```

## File System Safety

### Prevent path traversal via user-controlled path components

Package IDs and version strings are untrusted input. Validate them and verify the resolved path stays within the store root before any file operation.

```csharp
// ✗ Vulnerable — a crafted ID such as "../../etc/passwd" can escape the store
var dir = Path.Combine(_storePath, identity.Id, identity.Version);

// ✓ Validate and verify containment
var dir = Path.Combine(_storePath, identity.Id.ToLowerInvariant(), identity.Version.ToLowerInvariant());
var fullDir = Path.GetFullPath(dir);
var root = Path.GetFullPath(_storePath) + Path.DirectorySeparatorChar;
if (!fullDir.StartsWith(root, StringComparison.Ordinal))
    throw new ArgumentException("Package identity escapes store path.");
```

- [ ] All path components derived from user input are validated (no separators, `..`, or rooted paths).
- [ ] Resolved paths are checked for containment within the store root before every file operation.
- [ ] The store root path is normalized (trailing separators trimmed) before comparison.

## Atomicity and Partial Writes

### Write to a temp file, then rename atomically

Never write directly to the final file path. Write to a `.tmp` name, then rename into place. Use the **commit marker** pattern: write `.nuspec` and `.sha512` temp files before moving `.nupkg` last.

```csharp
// ✗ A crash mid-write leaves a permanently corrupt partial file
await File.WriteAllBytesAsync(finalPath, data, cancellationToken);

// ✓ Atomic: final file only visible after all data is on disk
await File.WriteAllBytesAsync(tmpPath, data, cancellationToken);
File.Move(tmpPath, finalPath, overwrite: false);  // no-overwrite
```

- [ ] All new package files are written to a `.tmp` path and renamed atomically into the final path.
- [ ] `.nupkg` is moved last (commit marker); `.nuspec` and `.sha512` are moved first.
- [ ] All temp files are cleaned up on the `AlreadyExists` path and on any exception path.
- [ ] `File.Move` / `FileInfo.MoveTo` uses `overwrite: false` to prevent silently replacing an existing file.

## Concurrency

### Catch IOException from FileMode.CreateNew — do not rely on File.Exists

```csharp
// ✗ Race-prone — a second writer can still throw IOException between Exists() and CreateNew
if (File.Exists(path)) return AlreadyExists;
using var fs = new FileStream(path, FileMode.CreateNew);

// ✓ Let FileMode.CreateNew throw, then translate the exception
try
{
    using var fs = new FileStream(path, FileMode.CreateNew);
    // write...
}
catch (IOException) when (File.Exists(path))
{
    return PackageStoreResult.AlreadyExists;
}
```

### Never share a single SqliteConnection across concurrent callers

`Microsoft.Data.Sqlite` does not support concurrent operations on a single connection. Each concurrent consumer must open its own connection.

- [ ] No check-then-act file-existence patterns (replace with `FileMode.CreateNew` + `IOException` catch).
- [ ] `SqliteConnection` is not shared across concurrent consumers; a connection factory or per-operation connection is used.

## Exception Types

### Custom public exception types — provide the three standard constructors

CA1032 requires parameterless, `(string message)`, and `(string message, Exception innerException)` constructors. With `TreatWarningsAsErrors=true`, missing them fails the build.

```csharp
// ✗ Triggers CA1032
public class PackageIntegrityException : Exception
{
    public PackageIntegrityException(string message) : base(message) { }
}

// ✓ All three constructors present
public class PackageIntegrityException : Exception
{
    public PackageIntegrityException() { }
    public PackageIntegrityException(string message) : base(message) { }
    public PackageIntegrityException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

- [ ] Every custom public exception type has the three standard constructors.

## Timestamp / SQLite Storage

### Normalize timestamps to UTC before storing in TEXT columns

SQLite's `MAX()` on `TEXT` is lexicographic. Timestamps stored with non-UTC offsets (e.g., `+02:00`) sort incorrectly against UTC values (`Z`). Always store the UTC instant.

```csharp
// ✗ Stores original offset — MAX() can return the wrong row
cmd.Parameters.AddWithValue("$at", dto.ToString("O"));

// ✓ Normalize to UTC before storing
cmd.Parameters.AddWithValue("$at", dto.UtcDateTime.ToString("O"));
```

Parse with `CultureInfo.InvariantCulture` and `DateTimeStyles.RoundtripKind` to avoid culture-dependent failures.

- [ ] Timestamps stored in SQLite `TEXT` columns are normalized to UTC (`UtcDateTime.ToString("O")`).
- [ ] Timestamp parsing uses `CultureInfo.InvariantCulture` and `DateTimeStyles.RoundtripKind`.

## Database Lifecycle

### Apply schema migrations in the production initialization path

Schema migrations must run when the database connection is first opened for production use — not only during test setup. A test-only `ApplyAsync` call means production databases start without any tables.

- [ ] Schema migration is called from the production `OpenAsync` / startup path, not only from test `InitializeAsync`.

## Test Quality

- [ ] Test method names match the **actual production method** under test (e.g., `OpenNupkgAsync_StoredPackage_ReturnsSameBytes`, not `Read_StoredPackage_ReturnsSameBytes`).
- [ ] Every new public method has at least one test; no untested public surface area.
- [ ] Timestamp-sensitive tests include at least one non-UTC offset scenario (e.g., `+02:00`) to validate ordering logic.
- [ ] PR description test list (count and names) accurately reflects the tests that are actually in the commit.

## NuGet v3 Protocol

- [ ] All required endpoints return correct HTTP status codes (200, 404, 304).
- [ ] Missing packages return 404, never 500.
- [ ] ETag and `Last-Modified` headers are set on package download responses.
- [ ] `Content-Type: application/octet-stream` (or `application/zip`) for `.nupkg` responses.
- [ ] New endpoints are covered by integration tests using a real `NuGet.Protocol` client.

## Package Store / Immutability

- [ ] No existing `.nupkg` or `.nuspec` file is overwritten by new code.
- [ ] SHA-512 hash is stored at download time and verified on serve.
- [ ] Retention purge only fires under an explicit policy trigger — not during normal request handling.

## Security

- [ ] No secrets, tokens, or credentials committed.
- [ ] No new HTTP/TLS configuration that downgrades security.
- [ ] No sensitive data logged (credentials, package content).
- [ ] `persist-credentials: false` retained on `actions/checkout` steps.
- [ ] Vulnerability audit (`dotnet list Arbor.DevPackages.slnx package --vulnerable --include-transitive`) passes with no findings.
- [ ] Upstream credentials are read from environment variables or OS credential store only.

## Dependencies

- [ ] New NuGet packages have a license compatible with MIT.
- [ ] New packages are declared in `Directory.Packages.props` (not inline in `.csproj`).
- [ ] New packages are documented in `THIRD_PARTY_NOTICES.md`.

## Code Structure Consistency

- [ ] New files are placed in the correct feature folder (vertical slice), not a type-based folder.
- [ ] New feature code lives entirely in its own folder — no edits to existing features required.
- [ ] No dead code introduced.

## General

- [ ] All tests pass (`dotnet test Arbor.DevPackages.slnx`).
- [ ] No compiler warnings introduced.
- [ ] No unrelated files modified.
- [ ] PR description explains *what* changed and *why*.
- [ ] Code coverage maintained or improved.
- [ ] PR description test list (count and names) matches the actual tests in the commit.
- [ ] Every new public method has at least one test.
- [ ] Timestamp-sensitive tests include at least one non-UTC offset scenario.

## Instruction Improvement Loop

- [ ] Instruction Retrospective block included in the PR description.
- [ ] Proposed instruction improvement applied to `.github/copilot-instructions.md` (or tracked as a GitHub issue).
