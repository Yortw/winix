# Changelog

All notable changes to **winix** (the suite installer) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `winix agents init|remove|status`: write/refresh/remove a marker-delimited, version-pinned discoverability pointer in a project's AGENTS.md/CLAUDE.md.

## [0.3.0] - 2026-05-10

### Changed
- `winix list --json` and `winix status --json` now emit to **stdout** (was stderr) per suite convention. Pipe-friendly for `jq`; matches `files`, `treex`, `whoholds`, `man`.
- `winix uninstall` is now idempotent — tools that are not installed are reported as `○ {tool} — not installed (no action)` and contribute exit 0, instead of failing with `✗` and exit 1. Re-running `winix uninstall` after a partial run is now safe.
- `winix install` no longer annotates "not in manifest" errors with a misleading `(via X)` package-manager hint. The annotation made the error look like a `--via` mismatch when the real cause was that the requested tool is not part of the suite manifest at all.

### Fixed
- `winix list` and `winix install` now see the canonical 22-tool manifest that ships with the binary, instead of whatever the most recently *published* GitHub release advertised. Pre-fix the loader fetched `https://github.com/Yortw/winix/releases/latest/download/winix-manifest.json` on every invocation; while v0.3 was unshipped that asset described only 11 tools, so `winix install` could see only half of the tools the user's binary supported. The fix bundles the canonical manifest beside the binary at `share/winix/winix-manifest.json` and prefers it over the network round-trip.
- `winix list` / `winix status` / `winix list --json` no longer report wrong installed versions for packages that have an upgrade pending. Pre-fix `WingetAdapter` parsed the version cell as the last whitespace-separated token on the row, which became the source name (`winix`) or the available version whenever winget emitted the 5-column "Name Id Version Available Source" shape. The parser now anchors on the Id column (right-to-left scan) and returns the next token, robust against multi-word names and the optional Available/Source columns.
- `EnsureBucket` (scoop) and `EnsureTap` (brew) now throw on a non-zero exit from the underlying `bucket add` / `tap add` invocation. Pre-fix the result was discarded and the methods unconditionally returned `true`, so `winix install` emitted a misleading "registered scoop bucket" / "registered brew tap" stderr line even when the underlying command failed (network down, git missing from PATH, repo unreachable). Failures now surface a "could not register …" warning via the caller's existing catch.
- Manifest cache no longer hangs the whole tool when the on-disk JSON is corrupt or has a future-dated mtime. A typed corrupt-cache fallback discards the bad cache and falls back to the bundled manifest with a stderr warning naming the corrupt source; an mtime sanity bound rejects future timestamps so a misconfigured clock or a copied-back file can't poison subsequent runs.
- `winix install`'s PATH-probe (`IsOnPath`) no longer spawns each candidate executable with `--version` and immediately kills it. The replacement is a PATHEXT-aware PATH walk that resolves `.exe` / `.cmd` / `.bat` extensions on Windows by directory listing only. Drops the spurious "killed process" diagnostic noise that appeared in some terminals during install runs.
- Top-level catch sites that previously piped framework `ex.Message` to user output now narrow to `ex.GetType().Name`. Under `InvariantGlobalization=true` (the AOT default) framework messages can be SR resource keys (`Arg_ParamName_Name`, `IO_PathNotFound_Path`) rather than English text — the leak class documented in `feedback_invariant_globalization_resource_keys.md`.
- Exit codes 126 (PM unavailable) and 127 (unknown subcommand / unsupported PM in `--via`) are now emitted as documented. Pre-fix all error paths collapsed to exit 1, defeating the documented POSIX-style exit-code contract that scripts and CI consumers rely on.
- `--version` output no longer carries the `+gitsha` SourceLink suffix. Users see plain `winix 0.3.0`, matching the suite-wide convention.

### Added
- `winix-manifest.json` is now bundled beside the binary at `share/winix/winix-manifest.json`. `winix --version`, `winix list`, and `winix status` work fully offline against the bundled manifest with no network round-trip — measured `winix --version` startup drops from ~104 ms to ~28 ms after the network round-trip is removed from the cold path, and the corresponding correctness fix above (canonical manifest beats whatever the latest published release advertised) is the more important effect.
- Per-user network manifest cache at `%LOCALAPPDATA%/winix/` (Windows) or `$XDG_CACHE_HOME/winix/` (Unix). When network refresh succeeds, the result is persisted; subsequent loads pick `max(cache, bundle)` by mtime so users see between-release manifest updates without waiting for the next binary release. Cache is self-healing on corruption and bounded against future mtimes.
- `winix list` and `winix uninstall` now emit a `winix: querying <pm>…` progress line on stderr immediately before each package manager's bulk query, so users on slow PMs (winget) see that work is in progress. Suppressed under `--json`.

### Changed (perf)
- `winix list`, `winix status`, and `winix uninstall` now use a single bulk subprocess per package manager (`winget list`, `scoop list`, `brew list --versions`, `dotnet tool list -g`) instead of one filtered subprocess per tool. On real winget this drops wall-time from **5-7 minutes to ~8 seconds** for the 22-tool manifest. The previous implementation issued up to 88 filtered subprocess calls (22 tools × 2 PMs × `IsInstalled` + `GetInstalledVersion`); each `winget list --id X --exact` was measured at 7-19 seconds.

### Internal
- `Cli.RunAsync` library seam extracted (Program.cs is now a one-line forwarder). Orchestration contracts (`--via` whitelist, exit-code routing 126/127, `--json` to stdout, `(via X)` annotation drop on `not in manifest` errors, idempotent uninstall) are now testable without process spawning.

### Documentation
- `winix.1` regenerated; README + agent-guide synchronised. Notable additions: `--color` and `--json` flags now documented in OPTIONS (were previously omitted from the man page); `winix uninstall` correctly classified as state-mutating (was mis-listed as read-only alongside `list`/`status`); new "Manifest Sources" section explaining the bundled-manifest + cache + network-refresh precedence; first-run notice that `EnsureBucket` / `EnsureTap` may register a scoop bucket or brew tap on the user's behalf during `winix install`.

## [0.2.0] - 2026-04-16

- Initial release.
