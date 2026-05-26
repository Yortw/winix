# Changelog

All notable changes to **files** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-09

### Changed (BREAKING)
- `--max-depth N` semantics: now matches GNU `find -maxdepth` (search root yielded as depth 0; `--max-depth 0` emits only the root path; `--max-depth N` includes up to N levels of children below the root). Pre-fix the search root was never yielded and `--max-depth 0` showed immediate children. Migration: subtract 1 from any existing `--max-depth` values.

### Changed
- `--ndjson` records no longer include envelope fields (`tool`, `version`, `exit_code`, `exit_reason`). Stream-level metadata is now emitted only via `--json`. Each NDJSON line carries only per-record fields (`path`, `name`, `type`, `size_bytes`, `modified`, `depth`, optional `is_text`).
- `size_bytes` field now emits JSON `null` for directory entries (was the sentinel `-1`).
- `modified` field now emits JSON `null` when not populated (was the zero-value timestamp `"0001-01-01T00:00:00.0000000+00:00"`).
- `--json` output now goes to **stdout** (was stderr) per suite convention. Pipe-friendly for `jq`; matches `man`, `winix`, `whoholds`, `treex`.
- `--older DURATION` description updated from "Modified before duration" to "Not modified within duration" — same behaviour, clearer phrasing.

### Fixed
- Walk errors (permission denied, vanishing directories, I/O failures) are now surfaced to stderr and the process exits 1 with `exit_reason: walk_error_partial` plus a populated `walk_errors[]` array on the `--json` envelope. Previously these were silently swallowed and the partial result list shipped with exit 0, contradicting the documented exit-1 contract.
- Path that exists but is a regular file (not a directory) now reports "not a directory" instead of the misleading "path not found", and surfaces `exit_reason: not_a_directory` in the `--json` envelope.
- Top-level `catch (Exception ex)` narrowed to specific types (`RegexParseException` → 125, `UnauthorizedAccessException` / `IOException` → 1). Pre-fix the bare catch piped framework `ex.Message` to user output, which under `InvariantGlobalization=true` could leak SR resource keys per `feedback_invariant_globalization_resource_keys.md`.
- Symlink cycle detection no longer falls back to the unresolved `Path.GetFullPath` on `ResolveLinkTarget` failure — the bare-catch fallback defeated cycle detection on the very directory whose target couldn't be resolved. Now records the failure as a walk error and skips recursion.
- `--version` output no longer carries the `+gitsha` SourceLink suffix the .NET SDK appends by default. Users see plain `files 0.3.0`, matching the suite-wide convention.

### Added
- Library seam `Winix.Files.Cli.Run` for orchestration testing without process spawning.
- `--json` summary envelope now includes a `walk_errors` array enumerating directories and files that could not be read during the walk. Each entry is `{"path": "...", "reason": "..."}`. Always present (empty array on success); non-empty when `exit_reason: "walk_error_partial"`.
- `--json` pre-walk error envelopes (`path_not_found`, `not_a_directory`) now carry an `error` field with the human-readable failure detail, plus empty `searched_roots` and `walk_errors` arrays for shape parity with success envelopes.
- `peep` added to the man page SEE ALSO list (composes with `files` for periodic file-system polling).

## [0.2.0] - 2026-04-16

### Added
- Manual page (`man 1 files`) ships with the package and is installed to `share/man/man1/` by scoop and the native installer.

## [0.1.0] - 2026-04-02

- Initial release.
