# Changelog

All notable changes to **man** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.4.0] - 2026-05-10

### Fixed
- Permission-denied page no longer leaks a stack trace and a framework SR resource-key message. `UnauthorizedAccessException` is now caught alongside `IOException` in the read pipeline and surfaced as `man: permission denied: <path>`.
- External pager dispatch now checks the child process's exit code, not just whether `Process.Start` succeeded. Pre-fix, after the F5 shell-dispatch (`cmd /c $PAGER` / `sh -c $PAGER`), a missing pager binary let the shell start cleanly and exit with 9009 (cmd) or 127 (sh) — `man` returned exit 0 with the user seeing only the shell's "not recognized" diagnostic and an empty page. Now any non-zero exit other than 130 (SIGINT user-quit) triggers fallback to the built-in pager with a stderr warning.
- `PageDiscovery.FindInSection` no longer silently shadows valid pages with corrupt or non-groff files in higher-priority MANPATH roots. A truncated bundled install, AV quarantine stub, or OneDrive placeholder ahead of the real page would render as garbage with exit 0. Added a cheap structural peek (`LooksLikeManPage`) that requires at least one groff macro line in the first 64 lines; falls through to the next candidate when the structural check fails.

### Changed
- `--path` / `--where` now reports paths that pass the structural peek, slightly stronger contract than "first matching path on MANPATH".

### Documentation
- `man.1` regenerated from `man.1.md`.
- AI agent guide JSON-routing fields and MANPATH section corrected.

### Internal
- `Cli.Run` library seam extracted, mirroring the precedent across other tier-2 tools. Pure helpers (`ResolveWidth`, `EscapeJsonString`, `ResolveExternalPager`) lifted into `Winix.Man` for direct unit-testing. Test count: 121 (was 66).

## [0.2.0] - 2026-04-16

- Initial release.
