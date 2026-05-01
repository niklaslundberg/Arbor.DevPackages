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
- Don't add unnecessary `async`/`await` when you can return the `Task`/`ValueTask` directly (expression-body delegation avoids allocating a state machine).
- Use `throw;` (not `throw ex;`) when rethrowing to preserve the stack trace.
- Custom public exception types must provide the three standard constructors: parameterless, `(string message)`, and `(string message, Exception innerException)`.
- New NuGet packages: verify MIT-compatible license, declare version in `Directory.Packages.props`, document in `THIRD_PARTY_NOTICES.md`.
- Test naming: `Method_Scenario_ExpectedResult` (e.g. `ProbeUpstream_WhenOffline_ReturnsOfflineStatus`); the method name must match the **actual production method under test**.
- New or changed production code must include test coverage. Aim for 100%; mark genuinely unreachable paths with `// coverage: unreachable`. The 80% line-coverage threshold is a hard build gate.
- Package data is immutable once stored; never overwrite `.nupkg` or `.nuspec` files.
- Write new files to a `.tmp` name and rename atomically into the final path; move `.nupkg` last (commit marker).
- Validate package ID/version before using in file paths; verify the resolved path stays within the store root.
- Normalize timestamps to UTC (`UtcDateTime.ToString("O")`) before storing in SQLite `TEXT` columns; parse with `CultureInfo.InvariantCulture` + `DateTimeStyles.RoundtripKind`.
- Never share a single `SqliteConnection` across concurrent callers; use a connection factory (one connection per unit of work).
- Apply schema migrations in the production initialization path, not only in test setup.

For the full set of rules, always defer to `.github/copilot-instructions.md`.
