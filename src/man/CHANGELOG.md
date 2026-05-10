# Changelog

All notable changes to **man** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

### Changed
- `--json` output now goes to **stdout** (was stderr) per suite convention. This is a pipeline contract change — `man --json ls | jq` now works without `2>&1` redirection. Matches `winix`, `whoholds`, `treex`, `files`, and the rest of the suite's structured-output tools.
- `--path` / `--where` now reports paths that pass a structural validity check, slightly stronger contract than "first matching path on MANPATH". Truncated installs, AV quarantine stubs, and OneDrive placeholder files no longer surface as the canonical location for a page.
- Exit codes now follow the documented contract: usage errors exit **2**, the tool's own internal failures exit **125**. Pre-fix several paths returned generic non-zero codes that conflated user error with internal error.

### Fixed
- `--json` output now escapes ASCII control characters (`0x00`–`0x1F`) per RFC 8259 §7. Pre-fix a page containing a literal control character in its name or rendered content produced invalid JSON that broke downstream parsers.
- Corrupt or truncated gzip-compressed pages no longer crash with a stack trace. The decompression and read pipeline catches `IOException` and the gzip framing exceptions, surfacing a friendly `man: corrupt or unreadable page: <path>` diagnostic and exit 125.
- External pager dispatch now honours compound `MANPAGER` / `PAGER` values like `less -R` or `less -FX`. Pre-fix the value was passed as the literal program name and the spawn failed silently; now compound values dispatch via `cmd /c $PAGER` on Windows and `sh -c $PAGER` on POSIX.
- External pager dispatch now checks the child process's exit code, not just whether `Process.Start` succeeded. After the shell-dispatch fix above, a missing pager binary let the shell start cleanly and exit with 9009 (cmd) or 127 (sh) — `man` returned exit 0 with the user seeing only the shell's "not recognized" diagnostic and an empty page. Now any non-zero exit other than 130 (SIGINT user-quit) triggers fallback to the built-in pager with a stderr warning.
- Bundled-pages discovery now also looks at `<exeDir>/share/man` (was just `<exeDir>/man`). The published layout uses the POSIX `share/man/man1/` convention, so the bundled fallback was unreachable for AOT installs and Scoop-deployed binaries.
- Permission-denied page no longer leaks a stack trace and a framework SR resource-key message. `UnauthorizedAccessException` is now caught alongside `IOException` in the read pipeline and surfaced as `man: permission denied: <path>`.
- `PageDiscovery.FindInSection` no longer silently shadows valid pages with corrupt or non-groff files in higher-priority MANPATH roots. A truncated bundled install, AV quarantine stub, or OneDrive placeholder ahead of the real page would render as garbage with exit 0. Added a cheap structural peek (`LooksLikeManPage`) that requires at least one groff macro line in the first 64 lines; falls through to the next candidate when the structural check fails.
- When every candidate file for a page fails the structural check (a corrupt `.gz` that decompresses to non-groff text is the canonical reproducer), `man` now emits `found N candidate file(s) for X but none appear to be valid groff man pages` plus the rejected paths and exits 125. Pre-fix this was indistinguishable from "no manual entry" exit 1, leaving users to guess whether the page was missing or broken.
- `--version` output no longer carries the `+gitsha` SourceLink suffix. Users see plain `man 0.3.0`, matching the suite-wide convention.

### Internal
- `Cli.Run` library seam extracted, enabling orchestration testing without process spawning.
- Pure helpers (`ResolveWidth`, `EscapeJsonString`, `ResolveExternalPager`) lifted into `Winix.Man` for direct unit-testing.
- `PageDiscovery.LastRejectedPaths` rejection-tracking field added so the orchestration layer can distinguish "no candidate" from "every candidate rejected".

### Documentation
- `MANPAGER`, `PAGER`, and `MANWIDTH` environment variables are now documented in `man.1` and the README.
- MANPATH separator description corrected — it is platform-dependent (`;` on Windows, `:` on POSIX), not "colon-separated".
- `--width` default documented accurately as "terminal width capped at 80" (was vaguely "terminal width").
- `man.1` regenerated from `man.1.md` to reflect all of the above.
- AI agent guide JSON-routing fields and MANPATH section corrected to match the actual emitter.

## [0.2.0] - 2026-04-16

- Initial release.
