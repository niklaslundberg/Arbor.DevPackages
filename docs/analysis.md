# Arbor.DevPackages — Analysis Report

**Date:** 2026-04-29  
**Status:** Pre-implementation analysis

---

## 1. Purpose

This document records the analysis performed before any implementation begins on **Arbor.DevPackages**, a local NuGet package server designed to support excellent offline and fast local package management.

The key goals stated in the issue are:

- Support **offline workflow** for package dependencies.
- **Automatic purging** of old packages using configurable retention policies.
- **Read-through support** for upstream NuGet package servers.
- **Online/offline detection** for upstream feeds.
- **Multiple local feeds** with different properties (e.g. allow/block pre-releases).
- **Fast downloads** from local storage.
- **Immutability**: never modify package data; deletion is allowed for local development housekeeping.
- **NuGet v3 protocol only**.
- **Usage statistics** to support retention decisions and insights.
- Technology: **.NET 10**, **C#**, cross-platform (Windows, Linux, macOS).
- **License: MIT**.

---

## 2. Prior Art Survey

### 2.1 Reviewed implementations

| Project | License | Protocol | Notes |
|---|---|---|---|
| [BaGetter](https://github.com/bagetter/BaGetter) | MIT | v2 + v3 | Popular, actively maintained, supports S3/Azure/GCS backends |
| [kekyo/nuget-server](https://github.com/kekyo/nuget-server) | Apache 2.0 | v2 + v3 | Lightweight, single-file approach |
| [Sleet](https://github.com/emgarten/Sleet) | MIT | v3 | Static-file NuGet feed generator, targets cloud storage |
| [LiGet](https://github.com/clearcodecn/liget) | Apache 2.0 | v2 + v3 | Docker-centric, abandoned |
| [Nexus Repository](https://www.sonatype.com/products/sonatype-nexus-repository) | Proprietary | v2 + v3 | Enterprise, read-through caching, paid for HA |
| [ProGet](https://inedo.com/proget) | Proprietary (free tier) | v2 + v3 | Rich feature set, Windows-first |
| [Verdaccio](https://verdaccio.org) | MIT | npm (analogous) | Useful UX patterns for offline-first proxy |
| [Artifactory](https://jfrog.com/artifactory/) | Proprietary | v2 + v3 | Enterprise, caching proxy |

### 2.2 Key takeaways from prior art

- **BaGetter** is the most useful reference for a lean MIT-licensed v3 server. Its plugin architecture for storage backends is worth studying. However, it does not support offline/online detection or per-feed retention policies natively.
- **Sleet** proves that a static-file approach to v3 feed generation is viable but lacks proxy/read-through functionality.
- The **NuGet v3 protocol** (JSON-based service index + flat-container) is well-documented and more amenable to caching and content-addressed storage than v2 (OData).

---

## 3. Target Architectural Concepts

### 3.1 Layers

```
┌─────────────────────────────────────────┐
│           NuGet Clients (dotnet, VS)    │
└───────────────────┬─────────────────────┘
                    │ HTTP (NuGet v3)
┌───────────────────▼─────────────────────┐
│           Local Server (ASP.NET Core)   │
│  ┌──────────┐  ┌──────────┐  ┌───────┐  │
│  │  Feed    │  │  Proxy   │  │ Stats │  │
│  │  Router  │  │  Layer   │  │  DB   │  │
│  └──────────┘  └────┬─────┘  └───────┘  │
│                     │ Online only         │
│  ┌──────────────────▼──────────────────┐ │
│  │         Local Package Store         │ │
│  │  (content-addressed, immutable)     │ │
│  └─────────────────────────────────────┘ │
└─────────────────────────────────────────┘
                    │ (when online)
┌───────────────────▼─────────────────────┐
│         Upstream NuGet Feeds            │
│  (nuget.org, private feeds, etc.)       │
└─────────────────────────────────────────┘
```

### 3.2 Core subsystems

| Subsystem | Responsibility |
|---|---|
| **Feed Router** | Map incoming requests to the correct local feed configuration |
| **Read-Through Proxy** | Fetch from upstream when a package is not cached locally |
| **Connectivity Probe** | Detect online/offline state per upstream; circuit-break gracefully |
| **Local Package Store** | Immutable, content-addressed storage for `.nupkg` and metadata |
| **Retention Engine** | Apply per-feed retention policies; schedule purge jobs |
| **Statistics Collector** | Record download events, package access timestamps |
| **API Layer** | Implement NuGet v3 endpoints (service index, package base, search, registration) |

---

## 4. Pros and Cons

### 4.1 Building a custom local server (vs. using an existing product)

| | Pros | Cons |
|---|---|---|
| **Custom** | Full control over retention, offline policy, and statistics schema | Higher initial effort and ongoing maintenance burden |
| | Can be kept minimal and targeted at local developer workflow | Risk of incomplete v3 protocol implementation |
| | MIT license, no licensing cost or restrictions | Security responsibility stays with maintainer |
| | Cross-platform .NET 10 native | Must track NuGet protocol changes |
| **Existing (e.g. BaGetter)** | Battle-tested v3 implementation | Less control over retention and offline behavior |
| | Community maintained | Dependency on upstream project decisions |
| | Broad storage backend support | May carry features/complexity not needed |

### 4.2 NuGet v3 protocol only

| Pros | Cons |
|---|---|
| Simpler API surface (JSON + static-style flat container) | Incompatible with very old tooling (VS 2015 and earlier) |
| Content-addressable by default (packages keyed by ID + version) | Must implement all required v3 endpoints to avoid client errors |
| Better cache semantics (ETag, 304) | Registration blobs can be large for packages with many versions |
| No OData dependency | |

### 4.3 Immutability of package data

| Pros | Cons |
|---|---|
| Prevents accidental corruption or overwrite | Cannot patch a published package; must publish a new version |
| Simplifies caching (once cached, always valid) | Disk usage grows monotonically until a retention policy fires |
| Aligns with NuGet.org semantics | Requires explicit delete path for retention; risk of deleting depended-upon packages |

### 4.4 Read-through proxy

| Pros | Cons |
|---|---|
| Transparent to clients; no workflow change | Upstream latency on first fetch |
| Enables offline use after first fetch | Must handle upstream authentication securely |
| Reduces repeated internet traffic | Upstream package can be deprecated or yanked after caching |
| Single source of truth for local development | Cache invalidation for non-versioned metadata (search indexes) is complex |

### 4.5 Offline / online detection

| Pros | Cons |
|---|---|
| Graceful degradation when internet is unavailable | Connectivity state is probabilistic; split-brain possible |
| Clear error messages when upstream is unreachable | Per-host probing adds request overhead |
| Supports air-gapped development scenarios | Must decide: fail fast or serve stale? Both have risks |

### 4.6 Retention policies

| Pros | Cons |
|---|---|
| Prevents unbounded disk growth | Wrong policy can purge packages still needed by active projects |
| Usage statistics enable smarter pruning | Statistics collection adds overhead and storage |
| Configurable per feed allows flexibility | Complex to configure; potential for misconfiguration |

### 4.7 Multiple feeds

| Pros | Cons |
|---|---|
| Different retention/pre-release policies per feed | Routing logic adds complexity |
| Mirrors real-world enterprise setups | Clients must be configured with the correct feed URL |
| Can isolate experimental packages | Cross-feed dependency resolution is out of scope for the server |

---

## 5. Trade-offs and Design Decisions

### 5.1 Storage backend

**Options:**

- **Local filesystem**: Simple, fast, no additional infrastructure. Risk: disk-full scenarios, no built-in replication.
- **SQLite**: Good fit for metadata and statistics. Proven in similar tools (BaGetter, Arbor.HttpClient). Risk: WAL-mode required for concurrent access.
- **PostgreSQL / SQL Server**: Enterprise-grade but heavy for a local developer tool.

**Recommended starting point:** Local filesystem for package blobs (`.nupkg`, `.nuspec`) + SQLite for metadata index and statistics. This matches the BaGetter storage-plugin pattern and allows future extension.

### 5.2 Concurrency model

**Options:**

- **Single-writer + multiple-reader**: Simplest; SQLite WAL handles this natively.
- **Fully concurrent**: Requires locking at the package-version level.

**Recommended:** Single-writer with async I/O throughout. NuGet clients typically do not write; writes only occur during proxy fetch-and-cache.

### 5.3 Connectivity probe strategy

**Options:**

- **DNS resolution**: Fast but unreliable (may hit corporate proxy).
- **HTTP HEAD request to upstream service index**: Accurate but adds latency.
- **Passive probe**: Mark upstream offline only when a fetch fails; retry on next request.

**Recommended:** Passive probe with exponential back-off. Active probe as an optional configurable enhancement.

### 5.4 NuGet v3 endpoint completeness

The minimum viable set of endpoints for `dotnet restore` and Visual Studio to work:

| Resource Type | Required |
|---|---|
| `PackageBaseAddress/3.0.0` | Yes — `.nupkg` and `.nuspec` download |
| `RegistrationsBaseUrl/3.6.0` | Yes — package metadata |
| `SearchQueryService/3.5.0` | Yes for VS browsing; optional for `dotnet restore` |
| `PackageDetailsUriTemplate/5.1.0` | No — links to package page |
| `ReportAbuseUriTemplate/3.0.0` | No |
| `SymbolPackagePublish/4.9.0` | No — out of scope for local dev |

**Recommended:** Implement the three required resource types first.

### 5.5 Statistics storage

**Options:**

- **In-memory ring buffer**: Fast, no persistence, lost on restart.
- **SQLite table**: Persistent, queryable, minor write overhead per download.
- **External sink (OpenTelemetry, InfluxDB)**: Powerful but heavy.

**Recommended:** SQLite with a configurable flush interval. Keep the schema minimal: `(package_id, version, feed_id, downloaded_at)`. Expose a simple `/stats` endpoint for insights.

### 5.6 Configuration

**Options:**

- **JSON/YAML file**: Familiar to .NET developers, easy to version-control.
- **Environment variables only**: 12-factor-friendly, harder to express feed lists.
- **Database-backed**: Runtime changes without restart, complex.

**Recommended:** `appsettings.json` + environment variable overrides (standard `Microsoft.Extensions.Configuration` layering). This is the idiomatic .NET 10 approach.

### 5.7 Pre-release handling per feed

Each feed should expose a `allow-prerelease` flag. The proxy layer respects this when forwarding search queries. Local clients pointing to a feed without `allow-prerelease` will not see or restore pre-release packages even if they exist in the local store from another feed.

---

## 6. Security Considerations

### 6.1 Package integrity

- Store SHA-512 hash of every cached `.nupkg` at download time.
- Re-verify hash on every served request (or at startup with a background task).
- Expose `X-Checksum-SHA512` header on download responses.

### 6.2 Authentication

- For a local developer tool, authentication is optional but should be architecturally supported.
- Design: plug-in `IAuthenticationHandler` interface; default implementation is no-auth.
- If enabled, support API key (simple token in header) as the first mechanism.

### 6.3 Upstream credential handling

- Upstream credentials (for private feeds) must never be stored in plaintext in configuration.
- Recommended: read from environment variables or OS credential store.
- Follow the same OS credential store pattern as outlined in `Arbor.HttpClient/docs/security-review.md`.

### 6.4 TLS

- Serve over HTTPS by default in production mode.
- Use ASP.NET Core's built-in Kestrel TLS with a developer certificate for local use.
- Never downgrade TLS below 1.2.

### 6.5 Dependency supply chain

- All NuGet dependencies must have MIT-compatible licenses.
- Run `dotnet list package --vulnerable --include-transitive` in CI on every build.
- Pin all direct dependency versions in `Directory.Packages.props`.

---

## 7. Cross-cutting Concerns

### 7.1 Observability

- Structured logging via `Microsoft.Extensions.Logging` (Serilog as sink adapter).
- Request/response correlation IDs on every HTTP interaction.
- Metrics endpoint (`/metrics`) for Prometheus scraping (optional, future).

### 7.2 Cancellation and async

- All async methods must accept and propagate `CancellationToken`.
- HTTP client operations to upstream must honor cancellation.

### 7.3 Cross-platform compatibility

- Avoid Windows-only APIs.
- File paths must use `Path.Combine` and never hard-coded separators.
- Case-sensitive filesystem support required (Linux default).

### 7.4 Testing strategy

- Unit tests: business logic (retention engine, connectivity probe, metadata parser).
- Integration tests: NuGet v3 endpoint compliance using a real `NuGet.Client` in-process.
- No UI; test via HTTP client against a hosted test server (`WebApplicationFactory`).
- Test naming: `Method_Scenario_ExpectedResult`.

---

## 8. Out of Scope (for initial implementation)

- NuGet v2 (OData) support.
- Symbol server (`.snupkg`).
- Push / publish API (write from client).
- Web UI for browsing packages.
- Multi-node / clustering.
- Docker image publishing (can be added later).
- License expression validation beyond MIT-compatibility check.

---

## 9. Recommended Technology Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 |
| HTTP server | ASP.NET Core (Kestrel) |
| Metadata storage | SQLite (via `Microsoft.Data.Sqlite`) |
| Package blob storage | Local filesystem |
| Configuration | `Microsoft.Extensions.Configuration` + `appsettings.json` |
| Logging | `Microsoft.Extensions.Logging` + Serilog |
| HTTP client (upstream proxy) | `HttpClient` with `IHttpClientFactory` |
| Testing | xUnit + AwesomeAssertions + `WebApplicationFactory` |
| CI | GitHub Actions |
| License | MIT |

---

## 10. Inspiration Checklist

Before finalising the design, review the following:

- [x] BaGetter storage backend plugin architecture
- [x] NuGet v3 protocol specification (https://learn.microsoft.com/nuget/api/overview)
- [x] Sleet static feed generation approach
- [x] Arbor.HttpClient guidelines and principles (adapted for this repo)
- [ ] BaGetter's handling of `PackageBaseAddress` flat-container
- [ ] NuGet client `NuGet.Protocol` library for feed testing in integration tests
- [ ] Offline workflow patterns from Verdaccio (npm equivalent)

---

## 11. Open Questions

1. **Retention policy semantics**: Should purge be triggered by age, by download count threshold, or by combined score? What happens when a project still references a purged package — should the server warn?
2. **Feed isolation**: Should packages downloaded via one feed be re-served by another feed on the same server, or is each feed a completely isolated namespace?
3. **Upstream authentication**: How should credentials for private upstream feeds be supplied without storing them in config files?
4. **Search index freshness**: How often should upstream search results be cached, and should the cache be invalidated on a schedule or on demand?
5. **Concurrency of retention**: Should purge jobs run during normal operation or only in maintenance windows?
6. **Stats schema evolution**: What minimal schema supports retention decisions now without locking us into a design that cannot be extended later?

---

*This document will be updated as decisions are made during the implementation phase.*
