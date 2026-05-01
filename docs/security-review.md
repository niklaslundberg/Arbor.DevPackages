# Security Review

This document records the initial security posture for Arbor.DevPackages and should be kept as a living reference for future PRs.

## Scope

- Application code in `src/`
- CI/CD workflows in `.github/workflows/`
- NuGet dependencies and transitive dependencies
- Package storage and integrity

## Security Principles

### 1. Package integrity

Every `.nupkg` received from an upstream feed must be hashed (SHA-512) at download time. The hash is stored alongside the package metadata in SQLite. On every subsequent serve, the hash is verified before streaming the file to the client.

**Required actions:**
- Reject packages whose hash does not match the stored value.
- Log a warning with the package ID, version, and feed name when a hash mismatch is detected.
- Never serve a package whose integrity cannot be confirmed.

### 2. Upstream credential handling

Credentials for private upstream feeds must never be stored in plaintext in configuration files or committed to source control.

**Required approach:**
- Read upstream credentials from environment variables (`NUGET_FEED_{NAME}_USERNAME`, `NUGET_FEED_{NAME}_PASSWORD` or `_TOKEN`).
- Alternatively, delegate to the OS credential store (Windows Credential Manager, macOS Keychain, Linux Secret Service via `libsecret`).
- Introduce an `IUpstreamCredentialProvider` interface; inject at startup via DI.

### 3. TLS

- The initial implementation uses HTTP only for local loopback developer use. This is acceptable only when the server is reachable exclusively on the loopback interface of a single developer machine.
- HTTP is **not** acceptable if the server is reachable from another machine, container, VM, or any non-loopback interface; those environments must use HTTPS.
- HTTPS support will be added in Iteration 13 as an opt-in configuration option using ASP.NET Core Kestrel with a developer certificate.
- Never configure `SslProtocols.None` or `SslProtocols.Ssl3` or `SslProtocols.Tls` (TLS 1.0).
- Minimum: TLS 1.2. Prefer TLS 1.3 where client supports it.

### 4. Logging

- Never log package content or raw binary data.
- Never log upstream credentials (username, password, API key, token).
- Log package IDs, versions, and feed names at `Information` level.
- Log upstream request failures at `Warning` or `Error` level with correlation ID.

### 5. Dependency audit

Run the following before every PR merge:

```shell
dotnet list Arbor.DevPackages.slnx package --vulnerable --include-transitive
```

Address any reported vulnerabilities before merging. Do not disable or skip this check.

### 6. CI workflow security

- Set `persist-credentials: false` on all `actions/checkout` steps.
- Keep job/workflow permissions minimal (use `permissions:` key at workflow or job level).
- Upload only required artifacts; set `retention-days: 14` on CI artifacts.
- Provide SHA-256 checksums for release binaries.

## Initial Dependency Review

No production dependencies have been added yet (pre-implementation phase). This section will be updated with the first dependency audit result after the solution is initialized.

## Guidelines for Future PRs

1. **Keep dependency auditing mandatory** — do not remove vulnerability audit steps from workflows.
2. **Prefer least privilege in workflows** — keep `persist-credentials: false` unless a step explicitly requires persisted git auth.
3. **Treat artifacts as a security surface** — upload only required artifacts; keep retention as short as practical.
4. **Review security-sensitive code paths during changes**:
   - Package download and storage
   - Upstream proxy requests
   - Retention purge (deletion)
   - Configuration loading (credential paths)
   - Statistics recording
5. **Pin action and tool versions** — prefer immutable action references (commit SHAs) when updating workflows.

## Future: Authentication for the Local Server

The initial implementation has no authentication (local developer tool). When authentication is needed:

- Implement an `IAuthenticationHandler` interface in `Arbor.DevPackages.Core`.
- First mechanism: API key in `X-NuGet-ApiKey` header.
- Store API key hashes (bcrypt or Argon2), never plaintext.
- Rate-limit failed authentication attempts.

---

*Adapted from [Arbor.HttpClient `docs/security-review.md`](https://github.com/niklaslundberg/Arbor.HttpClient/blob/main/docs/security-review.md).*
