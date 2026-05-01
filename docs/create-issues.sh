#!/usr/bin/env bash
# Creates one GitHub issue per plan iteration.
# Run from the repository root with: bash docs/create-issues.sh
# Requires the GitHub CLI (gh) and authentication: gh auth login

set -euo pipefail

REPO="niklaslundberg/Arbor.DevPackages"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 0 — Solution scaffold" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** A compilable, testable solution with no production features.

## Checklist
- [ ] Create `Arbor.DevPackages.slnx` solution file referencing all projects
- [ ] Create `Directory.Packages.props` with all NuGet package versions centrally pinned
- [ ] Create `Directory.Build.props` with `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- [ ] Create all eight projects (Core, Storage.Sqlite, Server, ServiceDefaults, AppHost, Core.Tests, Storage.Sqlite.Tests, Server.Tests), each targeting `net10.0`
- [ ] Wire `Arbor.DevPackages.ServiceDefaults` (AddServiceDefaults / OpenTelemetry / health checks)
- [ ] Wire `Arbor.DevPackages.AppHost` registering Server as an Aspire resource
- [ ] Wire `Arbor.DevPackages.Server/Program.cs` with AddServiceDefaults + MapDefaultEndpoints
- [ ] Add a passing smoke test (`Smoke_SolutionBuilds`) in each `*.Tests` project
- [ ] Create CI workflow (`.github/workflows/ci.yml`) running `dotnet build` and `dotnet test`
- [ ] Add vulnerability audit step in CI: `dotnet list Arbor.DevPackages.slnx package --vulnerable --include-transitive`
- [ ] Create `THIRD_PARTY_NOTICES.md` listing all third-party dependencies
- [ ] Run `dotnet test Arbor.DevPackages.slnx` — all green
- [ ] `dotnet build` — zero warnings

## TDD steps
1. Add CI workflow → `dotnet build` fails (no solution) → create solution → CI passes
2. Add smoke test in each test project → `dotnet test` fails → add test → passes

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 1 — Domain model and core interfaces" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Define the core abstractions that all subsystems depend on. No implementations.

## Checklist
- [ ] Define interfaces in `Arbor.DevPackages.Core`: `IPackageStore`, `IStatisticsCollector`, `IStatisticsReader`, `IRetentionPolicy`, `IRetentionScheduler`, `IConnectivityProbe`, `IUpstreamProxy`, `IUpstreamCredentialProvider`, `IFeedRouter`
- [ ] Define value types/records: `PackageIdentity`, `FeedConfiguration`, `DownloadEvent`, `PackageMetadata`, `RetentionDecision`
- [ ] Compile entire solution with no warnings

## Tests to write (in `Arbor.DevPackages.Core.Tests`)
```
PackageIdentity_WithSameIdAndVersion_AreEqual
PackageIdentity_WithDifferentVersion_AreNotEqual
FeedConfiguration_AllowPrerelease_DefaultsToFalse
```

## TDD steps
1. Write test for `PackageIdentity` equality → fails → define `record PackageIdentity(string Id, string Version)` → passes
2. Write test for `FeedConfiguration` → same pattern

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 2 — Local package store (filesystem)" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Implement `IPackageStore` backed by the local filesystem with SHA-512 integrity checking. No networking.

## Checklist
- [ ] Implement `FileSystemPackageStore : IPackageStore` in `Arbor.DevPackages.Core/Packages/`
  - Stores files at `{storePath}/{id}/{version}/{id}.{version}.nupkg` (lower-case)
  - Stores `.nuspec` alongside: `{id}.{version}.nuspec`
  - On write: computes SHA-512; stores hash in `{id}.{version}.sha512` sidecar
  - On read: re-computes SHA-512; compares with sidecar; throws `PackageIntegrityException` on mismatch; returns `null` if missing
  - Never overwrites existing package file — returns `AlreadyExists` result

## Tests to write (in `Arbor.DevPackages.Core.Tests`, using a temp directory)
```
Store_NewPackage_StoresNupkgAndNuspecAndHash
Store_SamePackageTwice_ReturnsAlreadyExists
Read_StoredPackage_ReturnsSameBytes
Read_MissingPackage_ReturnsNull
Read_TamperedPackage_ThrowsPackageIntegrityException
```

## TDD steps
1. Write `Store_NewPackage_StoresNupkgAndNuspecAndHash` → fails → implement `StoreAsync` → passes
2. Write `Read_StoredPackage_ReturnsSameBytes` → fails → implement `ReadAsync` → passes
3. Continue red-green-refactor for each remaining test

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 3 — Statistics (SQLite)" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Implement `IStatisticsCollector` and `IStatisticsReader` backed by SQLite.

## Checklist
- [ ] Create `download_events` table via inline migration (applied on first run)
- [ ] Implement `SqliteStatisticsCollector : IStatisticsCollector` in `Arbor.DevPackages.Storage.Sqlite`
  - `RecordDownloadAsync(PackageIdentity, CancellationToken)` — inserts one row
- [ ] Implement `SqliteStatisticsReader : IStatisticsReader`
  - `GetLastDownloadedAtAsync(PackageIdentity, CancellationToken)`
  - `GetDownloadCountAsync(PackageIdentity, CancellationToken)`
  - `GetLastDownloadAcrossAllPackagesAsync(CancellationToken)`

## Tests to write (in `Arbor.DevPackages.Storage.Sqlite.Tests`, using `:memory:` SQLite)
```
RecordDownload_NewPackage_InsertsRow
RecordDownload_SamePackageTwice_InsertsTwoRows
GetLastDownloadedAt_AfterRecording_ReturnsRecordedTimestamp
GetLastDownloadedAt_NeverDownloaded_ReturnsNull
GetDownloadCount_AfterThreeDownloads_ReturnsThree
GetLastDownloadAcrossAllPackages_ReturnsLatestTimestamp
```

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 4 — Retention engine" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Implement the age-based retention policy (30-day rule) and the retention scheduler (waits 5 minutes since last download).

## Checklist
- [ ] Implement `IClock` abstraction and `SystemClock` (wraps `DateTimeOffset.UtcNow`) for test-time injection
- [ ] Implement `AgeBasedRetentionPolicy : IRetentionPolicy` in `Arbor.DevPackages.Core/Retention/`
  - Constructor accepts `RetentionOptions` (configurable retention window, default 30 days)
  - `GetPurgeCandidatesAsync(IStatisticsReader, CancellationToken)` returns eligible packages
- [ ] Implement `RetentionScheduler : IRetentionScheduler, IHostedService`
  - Runs every hour (configurable)
  - Aborts if last download within 5 minutes
  - Logs full planned purge list before deleting
  - Calls `IPackageStore.DeleteAsync` for each candidate

## Tests to write (in `Arbor.DevPackages.Core.Tests`)
```
AgeBasedRetentionPolicy_PackageOlderThan30Days_IsEligible
AgeBasedRetentionPolicy_PackageDownloadedToday_IsNotEligible
AgeBasedRetentionPolicy_NeverDownloadedPackage_IsEligible
RetentionScheduler_WhenLastDownloadWithin5Minutes_DoesNotPurge
RetentionScheduler_WhenLastDownloadOlderThan5Minutes_PurgesEligiblePackages
RetentionScheduler_LogsPurgeCandidatesBeforeDeleting
```

Use in-memory doubles for `IStatisticsReader`, `IPackageStore`, and the system clock.

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 5 — NuGet v3 service index" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Implement `GET /v3/index.json` — the service discovery endpoint all NuGet clients call first.

## Checklist
- [ ] Create `ServiceIndexEndpoints.cs` registering `app.MapGet("/v3/index.json", ...)` returning service index JSON
- [ ] Build `@id` values dynamically from `HttpContext.Request.Host` + configured path prefix
- [ ] Wire `Program.cs`: `AddServiceDefaults()` → `MapDefaultEndpoints()` → `MapServiceIndex()`; HTTP only; listens on `http://localhost:5000` by default

## Tests to write (in `Arbor.DevPackages.Server.Tests`, using `WebApplicationFactory`)
```
GetServiceIndex_ReturnsOkWithCorrectVersionAndResources
GetServiceIndex_ResourceIds_ContainRequestHost
GetServiceIndex_ContentType_IsApplicationJson
```

Verify a real `NuGet.Protocol` client can load the service index from the test server.

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 6 — PackageBaseAddress (flat-container) endpoints" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Implement the endpoints that allow `dotnet restore` to download packages.

## Checklist
- [ ] Create `FlatContainerEndpoints.cs` with `MapGroup("/v3/flatcontainer")`:
  - `GET /v3/flatcontainer/{id}/index.json` — list of available versions
  - `GET /v3/flatcontainer/{id}/{version}/{id}.{version}.nupkg` — download `.nupkg`
  - `GET /v3/flatcontainer/{id}/{version}/{id}.{version}.nuspec` — download `.nuspec`
- [ ] Lower-case all `{id}` and `{version}` before processing
- [ ] Return `404` if package not found (upstream proxy wired in Iteration 8)
- [ ] Return `200` with `Content-Type: application/octet-stream` and `X-Checksum-SHA512: {hash}` for `.nupkg`
- [ ] Record download event in `IStatisticsCollector` before returning response
- [ ] ETag = SHA-512 hex; respond `304 Not Modified` on matching `If-None-Match`
- [ ] Add fakes (`InMemoryPackageStore`, `NoOpStatisticsCollector`) to `Arbor.DevPackages.Testing`

## Tests to write (in `Arbor.DevPackages.Server.Tests`)
```
GetVersionList_KnownPackage_ReturnsVersionArray
GetVersionList_UnknownPackage_Returns404
DownloadNupkg_KnownPackage_ReturnsOctetStreamWithHash
DownloadNupkg_UnknownPackage_Returns404
DownloadNupkg_WithMatchingEtag_Returns304
DownloadNupkg_RecordsDownloadStatistic
DownloadNuspec_KnownPackage_ReturnsXml
```

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 7 — RegistrationsBaseUrl (metadata) endpoints" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Implement package metadata browsing — used by Visual Studio and `dotnet add package`.

## Checklist
- [ ] Create `RegistrationEndpoints.cs` with `MapGroup("/v3/registration")`:
  - `GET /v3/registration/{id}/index.json` — registration page listing all versions
  - `GET /v3/registration/{id}/{version}.json` — leaf with metadata for a single version
- [ ] Implement `RegistrationIndexBuilder` — pure function building registration JSON from `IPackageStore` metadata; independently unit-testable
- [ ] JSON structure must conform to NuGet Registration v3.6.0 spec

## Tests to write (in `Arbor.DevPackages.Server.Tests`)
```
GetRegistrationIndex_KnownPackage_ReturnsValidJson
GetRegistrationIndex_UnknownPackage_Returns404
GetRegistrationLeaf_KnownPackage_ReturnsVersionMetadata
GetRegistrationLeaf_UnknownPackage_Returns404
GetRegistrationIndex_NuGetProtocolClient_CanDeserialize
```

The last test uses a real `NuGet.Protocol` `PackageMetadataResource` against the `WebApplicationFactory` server.

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 8 — Read-through proxy and connectivity probe" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** When a package is requested but not cached locally, fetch it from the configured upstream feed and store it before responding to the client.

## Checklist
- [ ] Implement `PassiveConnectivityProbe : IConnectivityProbe`
  - Marks upstream offline on first fetch failure; retries after 60-second back-off (configurable)
  - Tracks `DateTimeOffset? LastFailedAt` per upstream URL
- [ ] Implement `UpstreamHttpProxy : IUpstreamProxy` using `IHttpClientFactory`
- [ ] Implement `NoOpCredentialProvider : IUpstreamCredentialProvider`
- [ ] Update `FlatContainerEndpoints.DownloadNupkg` handler workflow:
  1. Check store → found → serve
  2. Not found → probe → offline within back-off → `404`
  3. Reachable → `IUpstreamProxy.FetchAndStoreAsync` → serve from store
  4. Fetch fails → mark offline → `502 Bad Gateway`

## Tests to write

In `Arbor.DevPackages.Core.Tests`:
```
ProbeUpstream_WhenFetchFails_MarksUpstreamOffline
ProbeUpstream_WhenOffline_ReturnsOfflineStatus
ProbeUpstream_AfterBackoffExpiry_AllowsRetry
```

In `Arbor.DevPackages.Server.Tests` (using WireMock or fake upstream):
```
DownloadNupkg_NotCachedUpstreamOnline_FetchesStoresAndServes
DownloadNupkg_NotCachedUpstreamOffline_Returns404
DownloadNupkg_UpstreamFetchFails_Returns502AndMarksOffline
```

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 9 — Search endpoint" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Implement `SearchQueryService/3.5.0` — used by Visual Studio's package browser.

## Checklist
- [ ] Create `SearchEndpoints.cs` registering `app.MapGet("/v3/search", ...)` reading from `UpstreamSearchCache` and filtering in-memory
- [ ] Implement `UpstreamSearchCache : IHostedService`
  - Fetches upstream search results on startup and every 30 minutes
  - Persists raw upstream search JSON in memory
  - Serves last cached results when upstream is offline (stale-if-error)
- [ ] Create `SearchCacheEndpoints.cs` registering `app.MapPost("/api/feeds/{feedId}/search-cache/refresh", ...)` for on-demand cache busting
- [ ] Filter by `q`, `prerelease`, `skip`, `take` from the in-memory cache

## Tests to write (in `Arbor.DevPackages.Server.Tests`)
```
Search_WithQuery_ReturnsMatchingPackages
Search_WithPrereleaseTrue_IncludesPrereleasePackages
Search_WithPrereleaseFalse_ExcludesPrereleasePackages
Search_WhenUpstreamOffline_ReturnsLastCachedResults
SearchCacheRefresh_Returns200
```

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 10 — Multiple feeds" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Support multiple named feeds, each with its own upstream URL and `AllowPrerelease` flag. All feeds share the same local package store (no isolation).

## Checklist
- [ ] Add `Feeds` array to `appsettings.json` with `Id`, `UpstreamUrl`, `AllowPrerelease` per feed
- [ ] Implement `FeedRouter : IFeedRouter` — looks up `FeedConfiguration` by ID
- [ ] Refactor all endpoint modules to accept `{feedId}` route segment and resolve `FeedConfiguration` via `IFeedRouter`
- [ ] Nest each feature's `MapGroup` under `/feeds/{feedId}`
- [ ] Add feed-aware search filtering (`AllowPrerelease`)

## Route structure after refactor
```
/feeds/{feedId}/v3/index.json
/feeds/{feedId}/v3/flatcontainer/...
/feeds/{feedId}/v3/registration/...
/feeds/{feedId}/v3/search
```

## Tests to write
```
FeedRouter_KnownFeedId_ReturnsFeedConfiguration
FeedRouter_UnknownFeedId_ReturnsNull
Search_FeedWithAllowPrereleaseTrue_IncludesPrerelease
Search_FeedWithAllowPrereleaseFalse_ExcludesPrerelease
ServiceIndex_PerFeed_ResourcesPointToCorrectFeedPath
```

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 11 — Stats read endpoint" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Expose a simple read endpoint for download statistics.

## Checklist
- [ ] Register `GET /api/stats` returning JSON: `{ "packages": [ { "id", "version", "downloadCount", "lastDownloadedAt" } ] }`
- [ ] Wire `IStatisticsReader` into the endpoint handler

## Tests to write (in `Arbor.DevPackages.Server.Tests`)
```
GetStats_ReturnsDownloadCountAndLastTimestamp
GetStats_WithNoDownloads_ReturnsEmptyArray
```

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 13 — System tests (end-to-end)" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Verify the real, fully-assembled application from the outside with zero fakes, mocks, or test doubles. A real `dotnet restore` subprocess runs against a real server process.

## Checklist
- [ ] Create `Arbor.DevPackages.SystemTests` project — no reference to Core, Storage.Sqlite, or Server
- [ ] Implement `ServerFixture` (`IAsyncLifetime`):
  1. Pick a free TCP port
  2. Write temporary `appsettings.json` (feed `nuget-org`)
  3. Write temporary package store directory; pre-seed if needed
  4. Start `Arbor.DevPackages.Server` as a real `Process` (`dotnet run` or published binary)
  5. Poll `GET /health` until `200 Healthy` (max 30 s timeout)
  6. Expose `BaseAddress` and store path
  7. On `DisposeAsync`: kill process and delete temp directories
- [ ] Implement `NuGetRestoreRunner`:
  1. Create isolated NuGet global packages cache (sets `NUGET_PACKAGES` env var)
  2. Write `nuget.config` with only `http://localhost:{port}/feeds/nuget-org/v3/index.json` as source
  3. Write minimal `.csproj` with `<PackageReference>` to `Serilog` at pinned version
  4. Run `dotnet restore` as subprocess; capture exit code, stdout, stderr, and wall-clock duration
- [ ] Commit `testdata/serilog/` with pinned `.nupkg`, `.nuspec`, and `.sha512` sidecar

## Tests to write
```
Restore_ViaProxy_WhenPackageCachedAndUpstreamUnreachable_SucceedsWithoutCallingUpstream
Restore_ViaProxy_WhenPackageNotCached_FetchesFromUpstreamAndSucceeds
```

Scenario 2 (offline) is written first (no internet needed).
Scenario 1 is tagged `[Trait("Category", "SystemTest_Online")]`.

## No new NuGet dependencies
System tests use only xUnit and AwesomeAssertions — already planned. No WireMock or mocking libraries.

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

gh issue create \
  --repo "$REPO" \
  --title "Iteration 12 — HTTPS support (deferred)" \
  --label "enhancement" \
  --body "$(cat <<'BODY'
Part of the implementation plan in `docs/plan.md`.

**Goal:** Add HTTPS as an opt-in configuration option. HTTP remains the default for local developer use.

**Note:** This iteration is intentionally deferred. Implement only after Iteration 13 (system tests) is complete and green.

## Checklist
- [ ] Use ASP.NET Core Kestrel developer certificate (`dotnet dev-certs`) in development mode
- [ ] Add Kestrel HTTPS configuration in `appsettings.json`:
  ```json
  { "Kestrel": { "Endpoints": { "Https": { "Url": "https://localhost:5001" } } } }
  ```
- [ ] Enforce minimum TLS 1.2; prefer TLS 1.3
- [ ] Document in README: how to trust the dev cert and configure the NuGet client for HTTPS

## Definition of Done
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] No vulnerable packages
- [ ] CI green
- [ ] PR description explains what changed and why
- [ ] `THIRD_PARTY_NOTICES.md` up to date
BODY
)"

echo "All issues created successfully."
