# Copilot Instructions

Behavioral guidelines for Arbor.DevPackages. Adapted from [Arbor.HttpClient](https://github.com/niklaslundberg/Arbor.HttpClient) — merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 0. Read All Markdown Files First

**At the start of every session, read all Markdown files in the repository before making any decisions or changes.**

| File | Purpose |
|---|---|
| `README.md` | Project overview and quick-start |
| `docs/analysis.md` | Pre-implementation analysis, design decisions, and open questions |
| `docs/review-checklist.md` | Common CodeQL / security review items to check before every PR |
| `docs/security-review.md` | Security posture, findings, and guidelines for future PRs |
| `.github/copilot-instructions.md` | This file |

## Repository Constants

| Constant | Value |
|---|---|
| Solution file | `Arbor.DevPackages.slnx` (to be created) |
| Test command | `dotnet test Arbor.DevPackages.slnx` |
| Vulnerability audit command | `dotnet list Arbor.DevPackages.slnx package --vulnerable --include-transitive` |

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them — do not pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.

## 3. Code Correctness

### Async / CancellationToken

- Every async method that performs I/O must accept `CancellationToken cancellationToken` as its last parameter and pass it downstream.
- Never use `Task.Run` to offload CPU work that isn't genuinely CPU-bound.
- Never `await` inside a `lock`; use `SemaphoreSlim` instead.

### Exception handling

- Use `throw;` (not `throw ex;`) when rethrowing to preserve the stack trace.
- Do not swallow exceptions silently. Log and rethrow or let them propagate.
- Only catch exceptions you can meaningfully handle at the call site.

### Null safety

- Enable nullable reference types globally (`<Nullable>enable</Nullable>`).
- Use `is null` / `is not null` rather than `== null` / `!= null`.
- Use `ArgumentNullException.ThrowIfNull` for public API parameter validation.

### Immutability

- Prefer `readonly` fields and `init`-only properties.
- Prefer immutable collections (`IReadOnlyList<T>`, etc.) in return types and properties.

### Date and time

- Use `DateTimeOffset` (not `DateTime`) for all timestamps to preserve timezone information.

## 4. Testing

- Test naming: `Method_Scenario_ExpectedResult` (e.g. `ProbeUpstream_WhenOffline_ReturnsOfflineStatus`).
- New or changed production code must include test coverage.
- Use `xUnit` + `AwesomeAssertions`.
- Use `WebApplicationFactory<TEntryPoint>` for HTTP endpoint integration tests.
- Test project boundaries must mirror library boundaries: tests for a library live in a dedicated test project referencing only that library and shared test helpers.
- Do not add cross-layer `<ProjectReference>` entries to an existing test project.

## 5. NuGet Package Management

- All NuGet dependencies must have a license compatible with MIT.
- Declare every package version in `Directory.Packages.props` — never inline versions in `.csproj` files.
- Document new third-party dependencies in `THIRD_PARTY_NOTICES.md`.
- Run the vulnerability audit command before every PR and address any findings.

### License compatibility

Compatible (permissive): MIT, Apache 2.0, BSD (2-clause, 3-clause), ISC, Unlicense.  
Incompatible (copyleft): GPL, LGPL, AGPL, EUPL, CDDL.  
Requires review: LGPL (if dynamically linked only), MPL 2.0.

## 6. Project Structure

Follow **vertical slice / feature-centric** organization rather than horizontal type-based layers.

```
src/
  Arbor.DevPackages.Core/           # UI-agnostic business logic
    Feeds/                          # Feed configuration, routing
    Proxy/                          # Read-through proxy, connectivity probe
    Packages/                       # Package store, hash verification
    Retention/                      # Retention engine and policies
    Statistics/                     # Usage statistics collection
  Arbor.DevPackages.Storage.Sqlite/ # SQLite implementations of Core abstractions
  Arbor.DevPackages.Server/         # ASP.NET Core host, NuGet v3 endpoints
  Arbor.DevPackages.Testing/        # Shared test doubles, fakes, in-memory implementations
  Arbor.DevPackages.Core.Tests/
  Arbor.DevPackages.Storage.Sqlite.Tests/
  Arbor.DevPackages.Server.Tests/
```

No `Models/`, `Services/`, `Abstractions/`, or `Helpers/` directories at the top level of any project.

## 7. NuGet v3 Protocol

- Implement the minimum viable endpoint set first:
  - `PackageBaseAddress/3.0.0` — `.nupkg` and `.nuspec` download
  - `RegistrationsBaseUrl/3.6.0` — package metadata
  - `SearchQueryService/3.5.0` — package search
- Return correct HTTP status codes: 200, 404, 304 (ETag/conditional GET).
- Never return a 5xx for a missing package; use 404.
- All endpoints must be tested with a real `NuGet.Protocol` client in integration tests.

## 8. Security

- No secrets, tokens, or credentials may be committed to source.
- Upstream credentials must be read from environment variables or OS credential store — never from plaintext config.
- Store SHA-512 hash of every cached `.nupkg` at download time and verify on serve.
- Serve over HTTPS by default; never downgrade TLS below 1.2.
- No sensitive data (credentials, package content) may be logged.
- `persist-credentials: false` on all `actions/checkout` workflow steps.

## 9. Immutability and Retention

- Once a package version is stored, its `.nupkg` and `.nuspec` files are never overwritten.
- Deletion is permitted only when triggered by a configured retention policy.
- Retention policy changes must not take effect immediately; log the planned purge list before deleting.
- Statistics must be updated atomically with the download record.

## 10. Commit and PR Standards

- Every commit message uses imperative mood, ≤ 72 characters, with issue reference when applicable.
- Every changed line must trace directly to the user's request — no unrelated edits.
- PR description explains *what* changed and *why*.
- All tests must pass before marking a PR ready for review.
- No compiler warnings introduced.

## 11. Instruction Improvement Loop

If you notice a gap in these instructions during a session:

1. Note the gap in the PR description under an **Instruction Retrospective** heading.
2. Propose the improvement as a change to this file.
3. Apply the change and include it in the same PR if practical.

---

*Adapted from [Arbor.HttpClient `.github/copilot-instructions.md`](https://github.com/niklaslundberg/Arbor.HttpClient/blob/main/.github/copilot-instructions.md).*
