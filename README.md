# Arbor.DevPackages

A local NuGet package server for excellent local package management and offline developer workflows.

> **Status:** All planned iterations (0–13) implemented — service index, flat-container, registration, search, stats, proxy, push, and HTTPS support are in place. See [`docs/analysis.md`](docs/analysis.md) for the design analysis and [`docs/plan.md`](docs/plan.md) for the iterative implementation plan.

## Goals

- **Offline workflow**: serve packages locally so `dotnet restore` works without internet access after the first fetch.
- **Read-through proxy**: transparently fetch from upstream NuGet feeds (e.g. nuget.org) and cache locally.
- **Online/offline detection**: gracefully degrade when upstream feeds are unreachable.
- **Multiple local feeds**: each feed can have independent properties (e.g. allow pre-releases, retention window).
- **Fast downloads**: serve from local disk; no round-trip to upstream for cached packages.
- **Immutability**: cached package data is never modified; purge (delete) is allowed for retention housekeeping.
- **NuGet v3 protocol only**: JSON-based service index and flat-container endpoints.
- **Usage statistics**: record download events per package/version to support retention decisions and insights.

## Technology

- **.NET 10** and **C#**, cross-platform (Windows, Linux, macOS).
- **ASP.NET Core** (Kestrel) for the HTTP server.
- **SQLite** for metadata and statistics storage.
- **MIT license**.

## Documentation

| Document | Purpose |
|---|---|
| [`docs/analysis.md`](docs/analysis.md) | Design analysis: pros/cons, trade-offs, design decisions, resolved open questions |
| [`docs/plan.md`](docs/plan.md) | Iterative TDD implementation plan — from scaffold to HTTPS |

## Inspiration

- [BaGetter](https://github.com/bagetter/BaGetter) — popular MIT-licensed NuGet server
- [kekyo/nuget-server](https://github.com/kekyo/nuget-server) — lightweight implementation
- [Sleet](https://github.com/emgarten/Sleet) — static NuGet v3 feed generator
- [Arbor.HttpClient](https://github.com/niklaslundberg/Arbor.HttpClient) — baseline for setup, guidelines, and principles
- [NuGet API overview](https://learn.microsoft.com/nuget/api/overview) — v3 protocol specification

## HTTPS support

HTTPS is an opt-in configuration option. HTTP on port 5000 is the default for local developer use.

### Enable HTTPS in development

1. **Trust the ASP.NET Core developer certificate** (one-time, per machine):

   ```shell
   dotnet dev-certs https --trust
   ```

   On Linux, run `dotnet dev-certs https` and follow the distribution-specific instructions to add the certificate to the trusted store.

2. **Verify the HTTPS endpoint** is present in `appsettings.json` (it is included by default):

   ```json
   {
     "Kestrel": {
       "Endpoints": {
         "Https": { "Url": "https://localhost:5001" }
       }
     }
   }
   ```

3. **Configure the NuGet client** to use the HTTPS feed URL. For example, in `NuGet.Config`:

   ```xml
   <packageSources>
     <add key="local-dev" value="https://localhost:5001/feeds/default/v3/index.json" />
   </packageSources>
   ```

   Or from the command line:

   ```shell
   dotnet nuget add source https://localhost:5001/feeds/default/v3/index.json --name local-dev
   ```

4. **To disable HTTPS** (HTTP only), remove the `Https` endpoint block from `appsettings.json` or override it in `appsettings.Development.json`.

> **Security note:** The minimum TLS version enforced by the server is TLS 1.2; TLS 1.3 is preferred when both sides support it.

## Observability (OpenTelemetry)

Logging, tracing, and metrics are wired up in `Arbor.DevPackages.ServiceDefaults` for every
environment (not just under Aspire):

- **Logging**: structured logs flow through the standard `ILogger` pipeline and are always
  written to the console; they are also exported via OTel once an OTLP endpoint is configured
  (see below).
- **Tracing**: ASP.NET Core and outgoing `HttpClient` spans.
- **Metrics**: ASP.NET Core, `HttpClient`, and .NET runtime metrics (GC, JIT, thread pool,
  exceptions) via `OpenTelemetry.Instrumentation.Runtime`.
- Every signal carries `service.name` / `service.version` (from the assembly's informational
  version, which embeds the git commit) / `service.instance.id` and a `deployment.environment`
  resource attribute.

Export to an OTLP collector (Jaeger, Grafana, Aspire dashboard, etc.) by setting
`OTEL_EXPORTER_OTLP_ENDPOINT`; without it, only console logging is active — no traces or
metrics are exported. Point the collector at a private network segment, never a public endpoint.

## Production deployment

### Framework-independent (self-contained) release artifacts

```shell
pwsh ./scripts/publish.ps1
```

Publishes single-file, self-contained builds of `Arbor.DevPackages.Server` for `win-x64`,
`linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64` (override with `-RuntimeIdentifiers`) into
`artifacts/release/`. Each platform gets a zipped archive, a `.sha256` checksum file, and the set
is summarized in `manifest.json`. "Self-contained" means the target machine does **not** need the
.NET runtime installed.

### Run as a Windows Service

```powershell
pwsh ./scripts/publish.ps1 -RuntimeIdentifiers win-x64
# Extract artifacts/release/Arbor.DevPackages.Server-<version>-win-x64.zip, then, elevated:
pwsh ./scripts/install-windows-service.ps1 -ExecutablePath C:\Apps\ArborDevPackages\Arbor.DevPackages.Server.exe
Start-Service -Name Arbor.DevPackages
```

`Program.cs` calls `UseWindowsService()`, which is a no-op everywhere except when the process is
actually started by the Windows Service Control Manager — the same published exe runs equally
well from the console. Use `scripts/uninstall-windows-service.ps1` to remove the service.

### Run under systemd (Linux)

`Program.cs` also calls `UseSystemd()`. Publish for `linux-x64`, extract the archive, and install
the example unit at [`scripts/arbor-devpackages.service`](scripts/arbor-devpackages.service) — see
the comments in that file for the full setup.

## License

MIT — see [LICENSE](LICENSE).
