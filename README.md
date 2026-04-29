# Arbor.DevPackages

A local NuGet package server for excellent local package management and offline developer workflows.

> **Status:** Pre-implementation — see [`docs/analysis.md`](docs/analysis.md) for the full analysis, design decisions, and trade-offs before any code is written.

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
| [`docs/analysis.md`](docs/analysis.md) | Pre-implementation analysis: pros/cons, trade-offs, design decisions, open questions |

## Inspiration

- [BaGetter](https://github.com/bagetter/BaGetter) — popular MIT-licensed NuGet server
- [kekyo/nuget-server](https://github.com/kekyo/nuget-server) — lightweight implementation
- [Sleet](https://github.com/emgarten/Sleet) — static NuGet v3 feed generator
- [Arbor.HttpClient](https://github.com/niklaslundberg/Arbor.HttpClient) — baseline for setup, guidelines, and principles
- [NuGet API overview](https://learn.microsoft.com/nuget/api/overview) — v3 protocol specification

## License

MIT — see [LICENSE](LICENSE).
