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

## Instruction Improvement Loop

- [ ] Instruction Retrospective block included in the PR description.
- [ ] Proposed instruction improvement applied to `.github/copilot-instructions.md` (or tracked as a GitHub issue).
