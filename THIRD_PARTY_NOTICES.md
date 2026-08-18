# Third-Party Notices

Arbor.DevPackages uses the following third-party packages. Each package retains its original license.

---

## Production Dependencies

### Microsoft.Data.Sqlite

- **Version:** 10.0.11
- **License:** MIT
- **Project:** https://github.com/dotnet/efcore
- **Used in:** `Arbor.DevPackages.Storage.Sqlite`

### Microsoft.Extensions.Http.Resilience

- **Version:** 10.5.0
- **License:** MIT
- **Project:** https://github.com/dotnet/extensions
- **Used in:** `Arbor.DevPackages.ServiceDefaults`

### Microsoft.Extensions.ServiceDiscovery

- **Version:** 10.5.0
- **License:** MIT
- **Project:** https://github.com/dotnet/aspire
- **Used in:** `Arbor.DevPackages.ServiceDefaults`

### Microsoft.Extensions.Hosting.WindowsServices

- **Version:** 10.0.0
- **License:** MIT
- **Project:** https://github.com/dotnet/runtime
- **Used in:** `Arbor.DevPackages.Server` (run as a Windows Service via `UseWindowsService`)

### Microsoft.Extensions.Hosting.Systemd

- **Version:** 10.0.0
- **License:** MIT
- **Project:** https://github.com/dotnet/runtime
- **Used in:** `Arbor.DevPackages.Server` (run under systemd via `UseSystemd`)

### OpenTelemetry.Exporter.OpenTelemetryProtocol

- **Version:** 1.15.3
- **License:** Apache 2.0
- **Project:** https://github.com/open-telemetry/opentelemetry-dotnet
- **Used in:** `Arbor.DevPackages.ServiceDefaults`

### OpenTelemetry.Extensions.Hosting

- **Version:** 1.15.3
- **License:** Apache 2.0
- **Project:** https://github.com/open-telemetry/opentelemetry-dotnet
- **Used in:** `Arbor.DevPackages.ServiceDefaults`

### OpenTelemetry.Instrumentation.AspNetCore

- **Version:** 1.15.2
- **License:** Apache 2.0
- **Project:** https://github.com/open-telemetry/opentelemetry-dotnet-contrib
- **Used in:** `Arbor.DevPackages.ServiceDefaults`

### OpenTelemetry.Instrumentation.Http

- **Version:** 1.15.1
- **License:** Apache 2.0
- **Project:** https://github.com/open-telemetry/opentelemetry-dotnet-contrib
- **Used in:** `Arbor.DevPackages.ServiceDefaults`

### OpenTelemetry.Instrumentation.Runtime

- **Version:** 1.15.1
- **License:** Apache 2.0
- **Project:** https://github.com/open-telemetry/opentelemetry-dotnet-contrib
- **Used in:** `Arbor.DevPackages.ServiceDefaults` (GC, JIT, thread pool, and exception metrics)

---

## Aspire Application Host

### Aspire.AppHost.Sdk (SDK)

- **Version:** 13.4.6
- **License:** MIT
- **Project:** https://github.com/dotnet/aspire
- **Used in:** `Arbor.DevPackages.AppHost` (as MSBuild SDK)

### Aspire.Hosting.AppHost (auto-referenced by Aspire.AppHost.Sdk)

- **Version:** 13.4.6
- **License:** MIT
- **Project:** https://github.com/dotnet/aspire
- **Used in:** `Arbor.DevPackages.AppHost`

---

## Test Dependencies

### Arbor.Aesculus.NCrunch

- **Version:** 3.9.0
- **License:** MIT
- **Project:** https://github.com/niklaslundberg/Arbor.Aesculus
- **Used in:** `Arbor.DevPackages.SystemTests` (NCrunch-compatible VCS root path discovery)

### AwesomeAssertions

- **Version:** 9.4.0
- **License:** Apache 2.0
- **Project:** https://github.com/AwesomeAssertions/AwesomeAssertions
- **Used in:** `*.Tests` projects

### coverlet.collector

- **Version:** 10.0.0
- **License:** MIT
- **Project:** https://github.com/coverlet-coverage/coverlet
- **Used in:** `*.Tests` projects

### Microsoft.AspNetCore.Mvc.Testing

- **Version:** 10.0.7
- **License:** MIT
- **Project:** https://github.com/dotnet/aspnetcore
- **Used in:** `Arbor.DevPackages.Server.Tests`

### Microsoft.NET.Test.Sdk

- **Version:** 18.5.1
- **License:** MIT
- **Project:** https://github.com/microsoft/vstest
- **Used in:** `*.Tests` projects

### NuGet.Packaging

- **Version:** 7.3.1
- **License:** Apache 2.0
- **Project:** https://github.com/NuGet/NuGet.Client
- **Used in:** `Arbor.DevPackages.Server` (reads package identity from .nupkg via `PackageArchiveReader`)

### NuGet.Protocol

- **Version:** 7.3.1
- **License:** Apache 2.0
- **Project:** https://github.com/NuGet/NuGet.Client
- **Used in:** `Arbor.DevPackages.Server.Tests`

### NuGet.Versioning

- **Version:** 7.3.1
- **License:** Apache 2.0
- **Project:** https://github.com/NuGet/NuGet.Client
- **Used in:** `Arbor.DevPackages.Server` (SemVer-ordered version lists)

### xunit

- **Version:** 2.9.3
- **License:** Apache 2.0
- **Project:** https://github.com/xunit/xunit
- **Used in:** `*.Tests` projects

### xunit.runner.visualstudio

- **Version:** 3.1.5
- **License:** Apache 2.0
- **Project:** https://github.com/xunit/visualstudio.xunit
- **Used in:** `*.Tests` projects
