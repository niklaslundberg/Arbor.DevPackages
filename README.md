# Arbor.DevPackages

A local NuGet package server for excellent local package management and offline developer workflows.

> **Status:** Pre-implementation — see [`docs/analysis.md`](docs/analysis.md) for the full analysis and [`docs/plan.md`](docs/plan.md) for the iterative implementation plan.

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
| [`docs/analysis.md`](docs/analysis.md) | Pre-implementation analysis: pros/cons, trade-offs, design decisions, resolved open questions |
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

## License

MIT — see [LICENSE](LICENSE).
