# Arbor.DevPackages — Implementation Plan

**Date:** 2026-05-01  
**Status:** Approved for implementation  
**Based on:** [`docs/analysis.md`](analysis.md) (all open questions resolved)

---

## Principles

1. **Test-Driven Development (TDD):** Every iteration begins by writing the failing tests. Production code is written only to make those tests pass.
2. **Smallest useful increment:** Each iteration delivers a working, tested, and releasable slice of functionality.
3. **Vertical slices:** New code lives inside its own feature folder; no horizontal type-based layers.
4. **No speculative code:** Only what the current iteration demands. Abstractions are introduced when they have at least two concrete implementations.
5. **HTTP only first:** HTTPS is deferred to a later iteration (the server targets local developer machines).

---

## Technology Decisions

### ASP.NET Core Minimal APIs

All HTTP endpoints are implemented using **ASP.NET Core Minimal APIs** (`app.MapGet`, `app.MapPost`, `app.MapGroup`). No MVC controllers are used.

Rationale:
- Leaner code — no `[ApiController]`, `[Route]`, or action-method boilerplate.
- Endpoint handlers are plain delegates or static methods, trivially unit-testable without HTTP infrastructure.
- Endpoint groups (`app.MapGroup("/v3/flatcontainer")`) keep route prefixes co-located with their handlers.
- Parameter binding is explicit and compile-time safe.

**Organisation pattern:** Each feature registers its own endpoints via an extension method on `IEndpointRouteBuilder`:

```csharp
// FlatContainerEndpoints.cs
public static class FlatContainerEndpoints
{
    public static IEndpointRouteBuilder MapFlatContainer(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v3/flatcontainer");
        group.MapGet("{id}/index.json", GetVersionListAsync);
        group.MapGet("{id}/{version}/{filename}", DownloadPackageFileAsync);
        return app;
    }
}
```

`Program.cs` wires them all:

```csharp
app.MapServiceIndex();
app.MapFlatContainer();
app.MapRegistration();
app.MapSearch();
app.MapStats();
```

Integration tests continue to use `WebApplicationFactory<Program>` — no change to test infrastructure.

### .NET Aspire

The solution uses **.NET Aspire (latest stable)** for the application host, observability defaults, and developer dashboard.

**Added projects:**

| Project | SDK | Purpose |
|---|---|---|
| `Arbor.DevPackages.AppHost` | `Aspire.Hosting.AppHost` | Declares and wires up the distributed application; entry point for `dotnet run` during development |
| `Arbor.DevPackages.ServiceDefaults` | `Microsoft.NET.Sdk` | Shared Aspire service defaults: OpenTelemetry tracing/metrics, health checks, service discovery client |

**What Aspire provides out-of-the-box (via `ServiceDefaults`):**
- Structured OpenTelemetry traces and metrics — replaces manual Serilog wiring for most scenarios.
- `/health` and `/alive` health-check endpoints on the server.
- Aspire Developer Dashboard (local UI at `http://localhost:15888`) — visualises logs, traces, and resource state during development.
- `IServiceCollection.AddServiceDefaults()` / `WebApplication.MapDefaultEndpoints()` pattern.

**What Aspire does not replace:**
- `IPackageStore`, retention, proxy, and statistics logic — these remain in `Core` and `Storage.Sqlite`.
- SQLite configuration — still in `appsettings.json`; Aspire does not manage the SQLite file.
- NuGet v3 endpoint logic.

**AppHost registration example:**

```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.Arbor_DevPackages_Server>("server");
builder.Build().Run();
```

**Aspire packages (to pin in `Directory.Packages.props`):**

| Package | Used in |
|---|---|
| `Aspire.Hosting.AppHost` | `AppHost` |
| `Microsoft.Extensions.ServiceDiscovery` | `ServiceDefaults`, `Server` |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `ServiceDefaults` |
| `OpenTelemetry.Extensions.Hosting` | `ServiceDefaults` |
| `OpenTelemetry.Instrumentation.AspNetCore` | `ServiceDefaults` |
| `OpenTelemetry.Instrumentation.Http` | `ServiceDefaults` |

---

## Project Structure

```
Arbor.DevPackages.slnx
Directory.Packages.props
THIRD_PARTY_NOTICES.md
testdata/
  serilog/                            # Pre-seeded .nupkg/.nuspec/.sha512 for system test scenario 2
src/
  Arbor.DevPackages.AppHost/          # Aspire app host — wires up services for local dev
  Arbor.DevPackages.ServiceDefaults/  # Shared Aspire defaults: OTel, health checks
  Arbor.DevPackages.Core/             # UI-agnostic business logic and interfaces
    Feeds/                            # Feed configuration, routing
    Proxy/                            # Read-through proxy, connectivity probe
    Packages/                         # Package store, hash verification
    Retention/                        # Retention engine and policies
    Statistics/                       # Usage statistics collection
  Arbor.DevPackages.Storage.Sqlite/   # SQLite implementations of Core abstractions
  Arbor.DevPackages.Server/           # ASP.NET Core host, NuGet v3 Minimal API endpoints
  Arbor.DevPackages.Testing/          # Shared test doubles, in-memory implementations
  Arbor.DevPackages.Core.Tests/
  Arbor.DevPackages.Storage.Sqlite.Tests/
  Arbor.DevPackages.Server.Tests/
  Arbor.DevPackages.SystemTests/      # End-to-end system tests: real server process + dotnet restore
```

---

## Iteration 0 — Solution scaffold

**Goal:** A compilable, testable solution with no production features.

### What to create

- `Arbor.DevPackages.slnx` solution file referencing all projects below.
- `Directory.Packages.props` with all NuGet package versions pinned centrally. Initial packages:
  - `Microsoft.NET.Sdk.Web` (SDK, no version)
  - `Microsoft.Data.Sqlite` (storage)
  - `Aspire.Hosting.AppHost` (AppHost SDK — version pinned to latest stable)
  - `Microsoft.Extensions.ServiceDiscovery` (ServiceDefaults + Server)
  - `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` (ServiceDefaults)
  - `xunit` + `xunit.runner.visualstudio` (testing)
  - `AwesomeAssertions` (assertion library)
  - `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`)
  - `coverlet.collector` (code coverage)
- All eight projects listed above, each with `.csproj` targeting `net10.0`.
- `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props`.
- `Arbor.DevPackages.ServiceDefaults` — calls `AddServiceDefaults()` extension wiring OpenTelemetry tracing, metrics, and health checks.
- `Arbor.DevPackages.AppHost` — registers `Arbor.DevPackages.Server` as an Aspire resource; entry point for `dotnet run` in development.
- `Arbor.DevPackages.Server/Program.cs` — calls `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()` before any feature endpoint registration.
- A passing smoke test in each `*.Tests` project:

  ```csharp
  [Fact]
  public void Smoke_SolutionBuilds() => Assert.True(true);
  ```

- CI workflow (`.github/workflows/ci.yml`) running `dotnet build` and `dotnet test` on every push.
- `THIRD_PARTY_NOTICES.md` listing all third-party dependencies with license and version.
- Vulnerability audit step in CI: `dotnet list Arbor.DevPackages.slnx package --vulnerable --include-transitive`.

### TDD steps

1. Add CI workflow → `dotnet build` fails (no solution file) → create solution → CI passes.
2. Add smoke test in each test project → `dotnet test` fails (no test yet) → add test → passes.

---

## Iteration 1 — Domain model and core interfaces

**Goal:** Define the core abstractions that all subsystems depend on. No implementations.

### Interfaces to define (in `Arbor.DevPackages.Core`)

| Interface | Location | Responsibility |
|---|---|---|
| `IPackageStore` | `Packages/` | Read/write `.nupkg` and `.nuspec` blobs; verify SHA-512 |
| `IStatisticsCollector` | `Statistics/` | Record a download event |
| `IStatisticsReader` | `Statistics/` | Query download counts and last-download timestamps |
| `IRetentionPolicy` | `Retention/` | Determine which packages are eligible for purge |
| `IRetentionScheduler` | `Retention/` | Trigger purge runs according to a schedule |
| `IConnectivityProbe` | `Proxy/` | Check whether an upstream feed is reachable |
| `IUpstreamProxy` | `Proxy/` | Fetch a package from upstream and store it locally |
| `IUpstreamCredentialProvider` | `Proxy/` | Supply credentials for a private upstream feed |
| `IFeedRouter` | `Feeds/` | Map an incoming request path to a `FeedConfiguration` |

### Value types / records to define

| Type | Location | Description |
|---|---|---|
| `PackageIdentity` | `Packages/` | Immutable record: `Id` (string), `Version` (string) |
| `FeedConfiguration` | `Feeds/` | Feed id, upstream URL, `AllowPrerelease` flag |
| `DownloadEvent` | `Statistics/` | `PackageIdentity`, `DownloadedAt` (DateTimeOffset) |
| `PackageMetadata` | `Packages/` | Identity, SHA-512 hash, stored `.nuspec` content |
| `RetentionDecision` | `Retention/` | `Purge` / `Keep`, reason string |

### TDD steps

1. Write a unit test that instantiates `PackageIdentity` and verifies equality by value → fails (type does not exist) → define `record PackageIdentity(string Id, string Version)` → passes.
2. Write a unit test for `FeedConfiguration` → same pattern.
3. Compile the entire solution with no warnings.

### Tests to write (in `Arbor.DevPackages.Core.Tests`)

```
PackageIdentity_WithSameIdAndVersion_AreEqual
PackageIdentity_WithDifferentVersion_AreNotEqual
FeedConfiguration_AllowPrerelease_DefaultsToFalse
```

---

## Iteration 2 — Local package store (filesystem)

**Goal:** Implement `IPackageStore` backed by the local filesystem with SHA-512 integrity checking. No networking.

### Production code (in `Arbor.DevPackages.Core`)

- `FileSystemPackageStore : IPackageStore`
  - Stores `.nupkg` files at `{storePath}/{id}/{version}/{id}.{version}.nupkg` (all lower-case).
  - Stores `.nuspec` files alongside: `{id}.{version}.nuspec`.
  - On write: computes SHA-512 of the incoming stream; stores the hash in a sidecar `{id}.{version}.sha512` file.
  - On read: re-computes SHA-512; compares with sidecar; throws `PackageIntegrityException` on mismatch; returns `null` if the package does not exist.
  - Never overwrites an existing package file — returns `AlreadyExists` result instead.

### Tests to write (in `Arbor.DevPackages.Core.Tests`, using a temp directory)

```
Store_NewPackage_StoresNupkgAndNuspecAndHash
Store_SamePackageTwice_ReturnsAlreadyExists
Read_StoredPackage_ReturnsSameBytes
Read_MissingPackage_ReturnsNull
Read_TamperedPackage_ThrowsPackageIntegrityException
```

### TDD steps

1. Write `Store_NewPackage_StoresNupkgAndNuspecAndHash` → fails → implement `FileSystemPackageStore.StoreAsync` → passes.
2. Write `Read_StoredPackage_ReturnsSameBytes` → fails → implement `ReadAsync` → passes.
3. Continue for each test case.

---

## Iteration 3 — Statistics (SQLite)

**Goal:** Implement `IStatisticsCollector` and `IStatisticsReader` backed by SQLite.

### Schema (applied on first run via inline migration)

```sql
CREATE TABLE IF NOT EXISTS download_events (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    package_id     TEXT    NOT NULL,
    version        TEXT    NOT NULL,
    downloaded_at  TEXT    NOT NULL  -- ISO 8601 UTC (DateTimeOffset.UtcNow.ToString("o"))
);

CREATE INDEX IF NOT EXISTS idx_download_events_pkg
    ON download_events (package_id, version, downloaded_at);
```

### Production code (in `Arbor.DevPackages.Storage.Sqlite`)

- `SqliteStatisticsCollector : IStatisticsCollector`
  - `RecordDownloadAsync(PackageIdentity, CancellationToken)` — inserts one row.
- `SqliteStatisticsReader : IStatisticsReader`
  - `GetLastDownloadedAtAsync(PackageIdentity, CancellationToken)` — returns latest `downloaded_at` or `null`.
  - `GetDownloadCountAsync(PackageIdentity, CancellationToken)` — returns `COUNT(*)`.
  - `GetLastDownloadAcrossAllPackagesAsync(CancellationToken)` — returns the latest `downloaded_at` across the entire table (used by the retention scheduler).

### Tests to write (in `Arbor.DevPackages.Storage.Sqlite.Tests`, using `:memory:` SQLite)

```
RecordDownload_NewPackage_InsertsRow
RecordDownload_SamePackageTwice_InsertsTwoRows
GetLastDownloadedAt_AfterRecording_ReturnsRecordedTimestamp
GetLastDownloadedAt_NeverDownloaded_ReturnsNull
GetDownloadCount_AfterThreeDownloads_ReturnsThree
GetLastDownloadAcrossAllPackages_ReturnsLatestTimestamp
```

### TDD steps

Follow the same red-green-refactor cycle for each test.

---

## Iteration 4 — Retention engine

**Goal:** Implement the age-based retention policy (30-day rule) and the retention scheduler (waits 5 minutes since last download).

### Business rules

- **Eligible for purge:** `last_downloaded_at < UtcNow - 30 days`.
- **Guard:** Do not start a purge run if `GetLastDownloadAcrossAllPackagesAsync()` returns a timestamp within the last 5 minutes.
- **Purge action:** Delete `.nupkg`, `.nuspec`, and `.sha512` sidecar files; log each deletion at `Information` level.
- **Purge ordering:** Log the full planned purge list before deleting any file.
- **No purge during request handling:** Purge runs only from `IRetentionScheduler`.

### Production code (in `Arbor.DevPackages.Core`)

- `AgeBasedRetentionPolicy : IRetentionPolicy`
  - Constructor accepts `RetentionOptions` (retention window, configurable; default 30 days).
  - `GetPurgeCandidatesAsync(IStatisticsReader, CancellationToken)` — returns packages eligible for purge.
- `RetentionScheduler : IRetentionScheduler, IHostedService`
  - Runs every hour (configurable).
  - Calls `GetLastDownloadAcrossAllPackagesAsync`; aborts if within last 5 minutes.
  - Calls `IRetentionPolicy.GetPurgeCandidatesAsync`; logs the planned list.
  - Calls `IPackageStore.DeleteAsync` for each candidate.

### Tests to write (in `Arbor.DevPackages.Core.Tests`)

```
AgeBasedRetentionPolicy_PackageOlderThan30Days_IsEligible
AgeBasedRetentionPolicy_PackageDownloadedToday_IsNotEligible
AgeBasedRetentionPolicy_NeverDownloadedPackage_IsEligible
RetentionScheduler_WhenLastDownloadWithin5Minutes_DoesNotPurge
RetentionScheduler_WhenLastDownloadOlderThan5Minutes_PurgesEligiblePackages
RetentionScheduler_LogsPurgeCandidatesBeforeDeleting
```

Use in-memory doubles for `IStatisticsReader`, `IPackageStore`, and the system clock.

### TDD steps

1. Implement `IClock` abstraction and `SystemClock` (wraps `DateTimeOffset.UtcNow`) to allow test-time injection.
2. Write each test against the clock double, making `RetentionScheduler` deterministic.

---

## Iteration 5 — NuGet v3 service index

**Goal:** Implement `GET /v3/index.json` — the service discovery endpoint that all NuGet clients call first.

### Endpoint

```
GET /v3/index.json
→ 200 OK, Content-Type: application/json
{
  "version": "3.0.0",
  "resources": [
    { "@id": "http://localhost:5000/v3/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
    { "@id": "http://localhost:5000/v3/flatcontainer/", "@type": "PackageBaseAddress/3.0.0" },
    { "@id": "http://localhost:5000/v3/search", "@type": "SearchQueryService/3.5.0" }
  ]
}
```

The `@id` values are built dynamically from `HttpContext.Request.Host` + configured path prefix.

### Production code (in `Arbor.DevPackages.Server`)

- `ServiceIndexEndpoints.cs` — registers `app.MapGet("/v3/index.json", ...)` returning the service index JSON.
- `Program.cs` — minimal `WebApplication`; calls `builder.AddServiceDefaults()`, then `app.MapDefaultEndpoints()`, then `app.MapServiceIndex()`; no authentication; HTTP only; listens on `http://localhost:5000` by default (overridable via `appsettings.json`).

### Tests to write (in `Arbor.DevPackages.Server.Tests`, using `WebApplicationFactory`)

```
GetServiceIndex_ReturnsOkWithCorrectVersionAndResources
GetServiceIndex_ResourceIds_ContainRequestHost
GetServiceIndex_ContentType_IsApplicationJson
```

### TDD steps

1. Write `GetServiceIndex_ReturnsOkWithCorrectVersionAndResources` → fails (no endpoint) → implement `ServiceIndexEndpoints.MapServiceIndex()` → passes.
2. Verify the real NuGet.Protocol client can load the service index from the test server.

---

## Iteration 6 — PackageBaseAddress (flat-container) endpoints

**Goal:** Implement the endpoints that allow `dotnet restore` to download packages.

### Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/v3/flatcontainer/{id}/index.json` | List of available versions for a package id |
| `GET` | `/v3/flatcontainer/{id}/{version}/{id}.{version}.nupkg` | Download a `.nupkg` |
| `GET` | `/v3/flatcontainer/{id}/{version}/{id}.{version}.nuspec` | Download a `.nuspec` |

Rules:
- All `{id}` and `{version}` values are lower-cased before processing.
- Return `404` if the package is not found locally and not available from upstream.
- Return `200` with `Content-Type: application/octet-stream` and `X-Checksum-SHA512: {hash}` on `.nupkg` responses.
- Record a download event in `IStatisticsCollector` before returning the response body.
- ETag is the SHA-512 hash (hex-encoded); respond with `304 Not Modified` if the client sends a matching `If-None-Match`.

### Production code (in `Arbor.DevPackages.Server`)

- `FlatContainerEndpoints.cs` — registers a `MapGroup("/v3/flatcontainer")` with three route handlers (version list, `.nupkg` download, `.nuspec` download). Route handlers inject `IPackageStore` and `IStatisticsCollector` via DI parameter binding.
- For now, if the package is not in the store, the handler returns `Results.NotFound()` (upstream proxy is wired in Iteration 8).

### Tests to write (in `Arbor.DevPackages.Server.Tests`)

```
GetVersionList_KnownPackage_ReturnsVersionArray
GetVersionList_UnknownPackage_Returns404
DownloadNupkg_KnownPackage_ReturnsOctetStreamWithHash
DownloadNupkg_UnknownPackage_Returns404
DownloadNupkg_WithMatchingEtag_Returns304
DownloadNupkg_RecordsDownloadStatistic
DownloadNuspec_KnownPackage_ReturnsXml
```

### TDD steps

Follow the red-green-refactor cycle. Use `Arbor.DevPackages.Testing` fakes for `IPackageStore` and `IStatisticsCollector`. Register fakes via `WebApplicationFactory` `ConfigureTestServices`.

---

## Iteration 7 — RegistrationsBaseUrl (metadata) endpoints

**Goal:** Implement package metadata browsing — used by Visual Studio and `dotnet add package`.

### Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/v3/registration/{id}/index.json` | Registration page listing all versions |
| `GET` | `/v3/registration/{id}/{version}.json` | Leaf — metadata for a single version |

The JSON structure must conform to the [NuGet Registration v3.6.0 spec](https://learn.microsoft.com/nuget/api/registration-base-url-resource).

### Production code (in `Arbor.DevPackages.Server`)

- `RegistrationEndpoints.cs` — registers `MapGroup("/v3/registration")` with two route handlers (index, leaf).
- `RegistrationIndexBuilder` — pure function: constructs the registration JSON from `IPackageStore` metadata; injectable and independently unit-testable.

### Tests to write (in `Arbor.DevPackages.Server.Tests`)

```
GetRegistrationIndex_KnownPackage_ReturnsValidJson
GetRegistrationIndex_UnknownPackage_Returns404
GetRegistrationLeaf_KnownPackage_ReturnsVersionMetadata
GetRegistrationLeaf_UnknownPackage_Returns404
GetRegistrationIndex_NuGetProtocolClient_CanDeserialize
```

The last test uses a real `NuGet.Protocol` `PackageMetadataResource` against the `WebApplicationFactory` server.

---

## Iteration 8 — Read-through proxy and connectivity probe

**Goal:** When a package is requested but not cached locally, fetch it from the configured upstream feed and store it before responding to the client.

### Connectivity probe strategy

- **Passive probe:** An upstream is marked *offline* the first time a fetch attempt fails. It is re-tried on the next incoming request after a 60-second back-off (configurable).
- `IConnectivityProbe` tracks a `DateTimeOffset? LastFailedAt` per upstream URL.
- `UpstreamHttpProxy : IUpstreamProxy` uses `IHttpClientFactory` to fetch from the upstream NuGet v3 flat-container URL.

### Workflow (inside `FlatContainerEndpoints.DownloadNupkg` handler)

```
1. Check IPackageStore → found → serve from local store.
2. Not found → check IConnectivityProbe → upstream offline within back-off window → return 404.
3. Upstream reachable → IUpstreamProxy.FetchAndStoreAsync(identity, cancellationToken).
4. Store succeeds → serve from local store (to re-verify hash).
5. Fetch fails → mark upstream offline; return 502 Bad Gateway (not 404, to distinguish "never existed" from "upstream error").
```

### Production code (in `Arbor.DevPackages.Core` and `Arbor.DevPackages.Server`)

- `UpstreamHttpProxy : IUpstreamProxy`
- `PassiveConnectivityProbe : IConnectivityProbe`
- `IUpstreamCredentialProvider` (no-op implementation `NoOpCredentialProvider`; interface defined in Iteration 1)

### Tests to write

In `Arbor.DevPackages.Core.Tests`:
```
ProbeUpstream_WhenFetchFails_MarksUpstreamOffline
ProbeUpstream_WhenOffline_ReturnsOfflineStatus
ProbeUpstream_AfterBackoffExpiry_AllowsRetry
```

In `Arbor.DevPackages.Server.Tests` (using a `WireMock` or fake upstream):
```
DownloadNupkg_NotCachedUpstreamOnline_FetchesStoresAndServes
DownloadNupkg_NotCachedUpstreamOffline_Returns404
DownloadNupkg_UpstreamFetchFails_Returns502AndMarksOffline
```

---

## Iteration 9 — Search endpoint

**Goal:** Implement `SearchQueryService/3.5.0` — used by Visual Studio's package browser.

### Endpoint

```
GET /v3/search?q={query}&skip={n}&take={n}&prerelease={true|false}
→ 200 OK
{
  "totalHits": 42,
  "data": [ ... ]
}
```

### Caching strategy

- On startup (and every 30 minutes via `IHostedService`), fetch the search results from the upstream search endpoint and cache them in memory.
- An on-demand refresh endpoint is available: `POST /api/feeds/{feedId}/search-cache/refresh` → `200 OK`.
- Results are filtered locally by `q`, `prerelease`, `skip`, and `take` from the cached upstream response.
- If upstream is offline, serve the last cached results (stale-if-error).

### Production code (in `Arbor.DevPackages.Server`)

- `SearchEndpoints.cs` — registers `app.MapGet("/v3/search", ...)` route handler; reads from `UpstreamSearchCache` and filters in-memory.
- `UpstreamSearchCache : IHostedService` — periodic refresh every 30 minutes; persists the raw upstream search JSON in memory.
- `SearchCacheEndpoints.cs` — registers `app.MapPost("/api/feeds/{feedId}/search-cache/refresh", ...)` for on-demand cache busting.

### Tests to write (in `Arbor.DevPackages.Server.Tests`)

```
Search_WithQuery_ReturnsMatchingPackages
Search_WithPrereleaseTrue_IncludesPrereleasePackages
Search_WithPrereleaseFalse_ExcludesPrereleasePackages
Search_WhenUpstreamOffline_ReturnsLastCachedResults
SearchCacheRefresh_Returns200
```

---

## Iteration 10 — Multiple feeds

**Goal:** Support multiple named feeds, each with its own upstream URL and `AllowPrerelease` flag. All feeds share the same local package store (no isolation).

### Configuration shape (in `appsettings.json`)

```json
{
  "Feeds": [
    {
      "Id": "nuget-org",
      "UpstreamUrl": "https://api.nuget.org/v3/index.json",
      "AllowPrerelease": false
    },
    {
      "Id": "myget",
      "UpstreamUrl": "https://www.myget.org/F/example/api/v3/index.json",
      "AllowPrerelease": true
    }
  ]
}
```

### Routing

Each feed gets its own URL prefix:

```
/feeds/{feedId}/v3/index.json
/feeds/{feedId}/v3/flatcontainer/...
/feeds/{feedId}/v3/registration/...
/feeds/{feedId}/v3/search
```

`IFeedRouter` maps the `feedId` path segment to the corresponding `FeedConfiguration`.

### Production code

- `FeedRouter : IFeedRouter` — looks up `FeedConfiguration` by ID.
- Refactor existing endpoint modules to accept feed ID as a route segment and resolve the correct `FeedConfiguration` via `IFeedRouter`. Each feature's `MapGroup` is nested under `/feeds/{feedId}`.
- Add feed-aware search filtering (`AllowPrerelease`).

### Tests to write

```
FeedRouter_KnownFeedId_ReturnsFeedConfiguration
FeedRouter_UnknownFeedId_ReturnsNull
Search_FeedWithAllowPrereleaseTrue_IncludesPrerelease
Search_FeedWithAllowPrereleaseFalse_ExcludesPrerelease
ServiceIndex_PerFeed_ResourcesPointToCorrectFeedPath
```

---

## Iteration 11 — Stats read endpoint

**Goal:** Expose a simple read endpoint for download statistics.

### Endpoint

```
GET /api/stats
→ 200 OK
{
  "packages": [
    { "id": "Newtonsoft.Json", "version": "13.0.3", "downloadCount": 42, "lastDownloadedAt": "2026-04-29T10:00:00Z" }
  ]
}
```

### Tests to write (in `Arbor.DevPackages.Server.Tests`)

```
GetStats_ReturnsDownloadCountAndLastTimestamp
GetStats_WithNoDownloads_ReturnsEmptyArray
```

---

## Iteration 13 — System tests (end-to-end)

**Goal:** Verify the real, fully-assembled application from the outside with zero fakes, mocks, or test doubles of any kind. A real `dotnet restore` subprocess runs against a real server process using only production code and production configuration. Nothing is substituted, intercepted, or simulated.

### Why system tests are different

| | Integration tests (`*.Server.Tests`) | System tests (`*.SystemTests`) |
|---|---|---|
| Server | `WebApplicationFactory` (in-process) | Real `dotnet run` process on a free TCP port |
| Upstream | WireMock / fake `HttpMessageHandler` | Real nuget.org (Scenario 1) or a genuinely unreachable URL — nothing listening on that port (Scenario 2) |
| Storage | Temp directory, real filesystem | Temp directory, real filesystem |
| NuGet client | `NuGet.Protocol` in-process | `dotnet restore` subprocess with custom `nuget.config` |
| Fakes / mocks | Allowed | **None — production code only** |
| Scope | Endpoint behaviour | Full round-trip including NuGet client protocol quirks |

**The key constraint for system tests:** no WireMock, no fake `HttpMessageHandler`, no in-memory substitutes. If a real upstream is not supposed to be called, the test ensures the server does not need to call it (because the package is already in the local store) — not by intercepting the call.

### New project

`Arbor.DevPackages.SystemTests` — no reference to `Arbor.DevPackages.Core`, `Storage.Sqlite`, or `Server`. The project boundary is enforced: the only way to interact with the server is over HTTP on a real socket.

### Test infrastructure (shared helpers in `Arbor.DevPackages.SystemTests`)

- `ServerFixture` — xUnit `IAsyncLifetime` class:
  1. Picks a free TCP port.
  2. Writes a temporary `appsettings.json` configuring one feed (`nuget-org`) with the upstream URL appropriate to the scenario (real nuget.org or a dead localhost URL).
  3. Writes a temporary package store directory and, if needed, pre-seeds it with committed test assets.
  4. Starts `Arbor.DevPackages.Server` as a real `Process` (`dotnet run` or the published binary) using the temp config.
  5. Polls `GET /health` until the server responds `200 Healthy` (max 30 s timeout).
  6. Exposes `BaseAddress` and the store directory path for use in tests.
  7. On `DisposeAsync`: kills the process and deletes temp directories.

- `NuGetRestoreRunner` — helper that:
  1. Creates a temporary directory as the NuGet global packages cache (isolated per run; sets `NUGET_PACKAGES` env var).
  2. Writes a `nuget.config` that declares **only** `http://localhost:{port}/feeds/nuget-org/v3/index.json` as the package source (no fallback to nuget.org directly, no other sources).
  3. Writes a minimal `.csproj` with a single `<PackageReference>` to `Serilog` at the pinned version.
  4. Runs `dotnet restore {project}` as a subprocess, captures stdout, stderr, exit code, and wall-clock duration.

### Scenario 1 — Passthrough to real nuget.org

**Name:** `Restore_ViaProxy_WhenPackageNotCached_FetchesFromUpstreamAndSucceeds`

**Setup:** `ServerFixture` configured with upstream = `https://api.nuget.org/v3/index.json`; empty local store.

**Steps:**
1. Run `dotnet restore` via `NuGetRestoreRunner`.
2. Assert: exit code `0`.
3. Assert: `serilog.{version}.nupkg` exists in the temp NuGet global packages cache.
4. Assert: the server's local store directory contains the fetched `.nupkg` and `.sha512` sidecar — confirming the server cached the package locally after proxying it.

**Why Serilog:** well-known, stable, MIT-licensed, small download, no unusual dependencies; if it restores correctly the full proxy pipeline works end-to-end.

**Network note:** this test requires outbound internet access. Tag it `[Trait("Category", "SystemTest_Online")]` so it can be excluded in air-gapped CI.

### Scenario 2 — Cached package, upstream unreachable

**Name:** `Restore_ViaProxy_WhenPackageCachedAndUpstreamUnreachable_SucceedsWithoutCallingUpstream`

**Setup:** `ServerFixture` configured with upstream = `http://localhost:{unusedPort}` (a port on which nothing is listening — a genuine connection refusal, not a mock). The server's local store is pre-seeded with the committed test assets before the server process starts.

**Steps:**
1. `ServerFixture` copies `testdata/serilog/` into the temp store directory before starting the server.
2. Run `dotnet restore` via `NuGetRestoreRunner` with a fresh NuGet global packages cache (so the client must contact the server).
3. Assert: exit code `0`.
4. Assert: wall-clock duration of `dotnet restore` < 5 seconds (no connection timeouts to a dead upstream).

**Why the timing assertion:** distinguishes "server served the cached package immediately" from "server attempted upstream, waited for TCP timeout (~20 s), then fell back to the local store". Correct behaviour means the server never attempts to open a connection to the upstream at all when the package is already in the local store.

**Why no WireMock here:** the upstream is genuinely unreachable (port with nothing listening). If the server incorrectly attempts to contact it, `dotnet restore` will either time out (failing the timing assertion) or fail outright (failing the exit-code assertion). No interception is needed.

### Test assets

`testdata/serilog/` — committed directory containing:
- `serilog.{version}.nupkg`
- `serilog.{version}.nuspec`
- `serilog.{version}.nupkg.sha512`

The pinned version must match the `<PackageReference>` version used in `NuGetRestoreRunner`'s probe project. Download the asset once and commit it; it never changes (packages on nuget.org are immutable).

### Tests to write (in `Arbor.DevPackages.SystemTests`)

```
Restore_ViaProxy_WhenPackageNotCached_FetchesFromUpstreamAndSucceeds
Restore_ViaProxy_WhenPackageCachedAndUpstreamUnreachable_SucceedsWithoutCallingUpstream
```

### TDD steps

1. Write `Restore_ViaProxy_WhenPackageCachedAndUpstreamUnreachable_SucceedsWithoutCallingUpstream` first (no outbound internet required) → fails (server does not exist yet) → implement the full server → passes.
2. Write `Restore_ViaProxy_WhenPackageNotCached_FetchesFromUpstreamAndSucceeds` (requires network) → passes when the upstream proxy (Iteration 8) is wired up.

### No new dependencies for this iteration

System tests require no additional NuGet packages beyond those already planned (xUnit, AwesomeAssertions). No WireMock or other mocking libraries are used here; those belong exclusively to integration tests.

---

## Iteration 12 — HTTPS support (deferred)

**Goal:** Add HTTPS as an opt-in configuration option. HTTP remains the default for local developer use.

- Use ASP.NET Core Kestrel's developer certificate (`dotnet dev-certs`) in development mode.
- Document in README: how to trust the dev cert and configure the NuGet client.
- Add Kestrel configuration in `appsettings.json`:

  ```json
  { "Kestrel": { "Endpoints": { "Https": { "Url": "https://localhost:5001" } } } }
  ```

- Never downgrade TLS below 1.2; prefer TLS 1.3.

---

## Test-Driven Development Workflow (summary)

Each iteration follows this exact sequence:

```
1. Write a failing test that describes the desired behaviour.
2. Run the test suite — confirm the new test fails with the expected error.
3. Write the minimum production code to make the test pass.
4. Run the test suite — confirm all tests pass.
5. Refactor (clean up, remove duplication) without breaking tests.
6. Repeat from step 1 for the next behaviour.
```

**Key practices:**
- Tests are committed **before** the production code that makes them pass, in separate commits when practical.
- Fake and in-memory implementations of interfaces live in `Arbor.DevPackages.Testing` so they can be shared across all test projects.
- Integration tests using `WebApplicationFactory<Program>` are written at the end of each iteration to validate the full HTTP stack.
- System tests in `Arbor.DevPackages.SystemTests` spin up a real server process and invoke `dotnet restore` as a subprocess. They are the final safety net: they are always run last and are the authoritative proof that the system works end-to-end.
- Online system tests (Scenario 1) are tagged `[Trait("Category", "SystemTest_Online")]` and may be skipped in air-gapped environments.
- The CI pipeline must be green after every committed iteration.

---

## Dependency checklist per iteration

Before adding any NuGet package:

1. Confirm the license is MIT-compatible.
2. Add the package version to `Directory.Packages.props` (never inline in `.csproj`).
3. Add the package to `THIRD_PARTY_NOTICES.md`.
4. Run `dotnet list Arbor.DevPackages.slnx package --vulnerable --include-transitive` — no findings allowed.

Planned third-party dependencies:

| Package | Version (to pin) | License | Used in |
|---|---|---|---|
| `Microsoft.Data.Sqlite` | 9.x | MIT | Storage |
| `Aspire.Hosting.AppHost` | latest stable | MIT | AppHost |
| `Microsoft.Extensions.ServiceDiscovery` | latest stable | MIT | ServiceDefaults, Server |
| `OpenTelemetry.Extensions.Hosting` | latest stable | Apache 2.0 | ServiceDefaults |
| `OpenTelemetry.Instrumentation.AspNetCore` | latest stable | Apache 2.0 | ServiceDefaults |
| `OpenTelemetry.Instrumentation.Http` | latest stable | Apache 2.0 | ServiceDefaults |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | latest stable | Apache 2.0 | ServiceDefaults |
| `xunit` | 2.x | Apache 2.0 | Testing |
| `xunit.runner.visualstudio` | 2.x | Apache 2.0 | Testing |
| `AwesomeAssertions` | 1.x | MIT | Testing |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.x | MIT | Integration tests |
| `WireMock.Net` | latest stable | Apache 2.0 | Integration tests |
| `coverlet.collector` | 6.x | MIT | Code coverage |

---

## Definition of Done (per iteration)

An iteration is complete when:

- [ ] All tests written in this iteration pass.
- [ ] All pre-existing tests still pass.
- [ ] `dotnet build` produces zero warnings.
- [ ] `dotnet list package --vulnerable --include-transitive` reports no findings.
- [ ] The CI pipeline is green.
- [ ] The PR description explains *what* changed and *why*.
- [ ] `THIRD_PARTY_NOTICES.md` is up to date.

---

*This plan is a living document. Update iteration goals and test lists as implementation reveals new requirements or constraints.*
