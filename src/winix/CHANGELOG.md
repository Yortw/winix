# Changelog

All notable changes to **winix** (the suite installer) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

### Fixed
- `winix list` / `winix status` / `winix list --json` no longer report wrong installed versions for packages that have an upgrade pending. Pre-fix `WingetAdapter` parsed the version cell as the last whitespace-separated token on the row, which became the source name (`winix`) or the available version whenever winget emitted the 5-column "Name Id Version Available Source" shape. The parser now anchors on the Id column (right-to-left scan) and returns the next token, robust against multi-word names and the optional Available/Source columns.
- `EnsureBucket` (scoop) and `EnsureTap` (brew) now throw on a non-zero exit from the underlying `bucket add` / `tap add` invocation. Pre-fix the result was discarded and the methods unconditionally returned `true`, so `winix install` emitted a misleading "registered scoop bucket" / "registered brew tap" stderr line even when the underlying command failed (network down, git missing from PATH, repo unreachable). Failures now surface a "could not register …" warning via the caller's existing catch.
- Manifest cache no longer hangs the whole tool when the on-disk JSON is corrupt or has a future-dated mtime. A typed cache fallback discards the corrupt cache and re-fetches; mtime sanity bound rejects future timestamps so a misconfigured clock or a copied-back file can't poison subsequent runs.
- `--version` output no longer carries the `+gitsha` SourceLink suffix. Users see plain `winix 0.3.0`, matching the suite-wide convention.

### Changed (perf)
- `winix list`, `winix status`, and `winix uninstall` now use a single bulk subprocess per package manager (`winget list`, `scoop list`, `brew list --versions`, `dotnet tool list -g`) instead of one filtered subprocess per tool. On real winget this drops wall-time from 5-7 minutes to ~8 seconds for a 22-tool manifest. The previous implementation issued up to 88 filtered subprocess calls (22 tools × 2 PMs × `IsInstalled` + `GetInstalledVersion`); each `winget list --id X --exact` was measured at 7-19 seconds.
- `winix list` and `winix uninstall` now emit a `winix: querying <pm>…` progress line on stderr immediately before each package manager's bulk query. Suppressed under `--json`.

### Internal
- `Cli.RunAsync` library seam extracted (Program.cs is now a one-line forwarder), mirroring the pattern across the other tier-2 tools. Orchestration contracts (`--via` whitelist, exit-code routing, `--json` to stdout, `(via X)` annotation drop on `not in manifest` errors) now testable without process spawning.

### Documentation
- `winix.1` regenerated; README + agent-guide synchronised on F1.1 + F1.2 + uninstall-is-destructive notes.

## [0.2.0] - 2026-04-16

- Initial release.
