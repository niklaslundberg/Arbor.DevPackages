# Codex Agent Instructions

> **Canonical source**: `.github/copilot-instructions.md` contains the full behavioral guidelines for this repository. Read it at the start of every session.
>
> **Sync note**: `CLAUDE.md` and `AGENTS.md` have identical body content (only the first-line title differs). If you modify the body of one, apply the same change to the other.

## Quick start

At the beginning of every session, read these files before making any decision or change:

| File | Purpose |
|------|---------|
| `.github/copilot-instructions.md` | Full behavioral guidelines (canonical) |
| `docs/analysis.md` | Pre-implementation analysis, design decisions, and open questions |
| `docs/review-checklist.md` | PR review items (CodeQL, security, dependencies) |
| `docs/security-review.md` | Security posture and guidelines |

## Hard stops (blocking rules)

- **All tests must pass** before every commit: `dotnet test Arbor.DevPackages.slnx`
- **Coverage threshold:** line coverage must be ≥ 80% — the build fails if it drops below this.
- **No secrets, tokens, or credentials** may be committed to source.
- **Every changed line must trace directly** to the user's request — no unrelated edits.
- **No HTTP/TLS configuration downgrade** may be introduced.
- **No sensitive data** (credentials, package content) may be logged.

## Key conventions

- Async methods that perform I/O must accept `CancellationToken cancellationToken` and pass it downstream.
- Use `throw;` (not `throw ex;`) when rethrowing to preserve the stack trace.
- New NuGet packages: verify MIT-compatible license, declare version in `Directory.Packages.props`, document in `THIRD_PARTY_NOTICES.md`.
- Test naming: `Method_Scenario_ExpectedResult` (e.g. `ProbeUpstream_WhenOffline_ReturnsOfflineStatus`).
- New or changed production code must include test coverage. Aim for 100%; mark genuinely unreachable paths with `// coverage: unreachable`. The 80% line-coverage threshold is a hard build gate.
- Package data is immutable once stored; never overwrite `.nupkg` or `.nuspec` files.

For the full set of rules, always defer to `.github/copilot-instructions.md`.
