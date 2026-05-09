# Changelog

All notable changes to **wargs** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

Substantial behaviour hardening across 17 fresh-eyes review rounds. Headlines below; full per-round detail in commit history.

### Changed
- `--ndjson` now streams lines as jobs complete rather than buffering until `Task.WhenAll`. Pipeline consumers (`wargs ... --ndjson | jq -c .`) see results as they happen instead of in one batch at the end.
- `--keep-order --ndjson` honours per-input ordering by holding back lines for parallel out-of-order completion. Pre-fix the streaming change had broken keep-order; now reconciled.
- `--describe` JSON schema now advertises every emitted field across every mode. Pre-fix several fields were emitted but undocumented, breaking the introspection contract for AI agents and automation.

### Fixed
- Every exit path now emits a complete JSON envelope when `--json` or `--ndjson` is set. Previously cancellation, broken-pipe-during-stdin, and several fault paths bypassed the envelope and produced bare stderr lines, corrupting downstream JSON consumers.
- Ctrl+C during stdin read no longer escapes the safety-net catches — `wargs` now exits 130 with a complete JSON envelope under `--json` / `--ndjson`.
- Output formatter now sanitises lone UTF-16 surrogates, preventing invalid-JSON output when child process stdout contains malformed multi-byte sequences.
- CRLF handling is now culture-aware to avoid splitting in unexpected places under non-en-US locales.
- Captured child output is bounded by an OOM cap with line-atomic merge; a runaway child no longer crashes wargs itself.
- `--confirm` invalid responses (anything other than `y`/`n`/Enter) are now rejected with a re-prompt rather than being silently treated as accept.
- `--dry-run` no longer emits subprocess output — pre-fix the `RunDryRun` path leaked stdout from a no-op invocation in some shells.
- File-descriptor exhaustion / OOM / stack-overflow conditions now surface the standard exit codes (125 / 126) per suite convention rather than escaping as unhandled exceptions.
- `--version` output no longer carries the `+gitsha` SourceLink suffix.

### Added
- Library seam `Winix.Wargs.Cli.Run` for orchestration testing without process spawning.
- Documentation: README, man page, and `--describe` text now explicitly cover exit code 130 (Ctrl+C), per-mode field schemas, and the restrictions section.

### Internal
- UTF-8 console adoption via `ConsoleEnv.UseUtf8Streams`.
- Standard `<PackageTags>` set on the NuGet package.
- Platform-skipped tests migrated to `SkippableFact + Skip.IfNot`.

## [0.2.0] - 2026-04-16

### Added
- Manual page (`man 1 wargs`) ships with the package and is installed to `share/man/man1/` by scoop and the native installer.

## [0.1.0] - 2026-04-02

- Initial release.
