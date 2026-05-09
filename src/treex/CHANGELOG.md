# Changelog

All notable changes to **treex** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-09

### Changed (BREAKING)
- `--max-depth N` semantics: now `depth ≤ N` with the search root at depth 0. `--max-depth 0` shows only the root; `--max-depth 1` shows root + immediate children. Previously `--max-depth 0` showed root + immediate children, contradicting the documented contract. Migration: subtract 1 from any existing `--max-depth` values.

### Changed
- `--ndjson` records no longer include envelope fields (`tool`, `version`, `exit_code`, `exit_reason`). Stream-level metadata is now emitted only via `--json`. Each NDJSON line carries only per-record fields (`path`, `name`, `type`, `size_bytes`, `modified`, `depth`) — ~35% bandwidth reduction on small trees, line-record convention compliance.
- `size_bytes` field now emits JSON `null` for directories without `--size` rollup (was the sentinel `-1`).
- `--json` output now goes to **stdout** (was stderr) per suite convention. Pipe-friendly for `jq` and matches `man`, `winix`, `whoholds`.
- `--older DURATION` description updated from "Modified before duration" to "Not modified within duration" — same behaviour, clearer phrasing.

### Fixed
- `--size --ndjson` now reports correct `total_size_bytes` in the summary envelope. Previously the NDJSON branch never accumulated sizes, so the summary always reported `0` regardless of actual file sizes.
- Walk errors (permission denied, vanishing directories, I/O failures) are now surfaced to stderr and the process exits 1 with `exit_reason: walk_error_partial`. Previously these were silently swallowed and the partial tree shipped with exit 0, contradicting the documented exit-1 contract.
- Directories that cannot be enumerated are now annotated with `[error opening dir]` in the rendered tree, matching `tree(1)` precedent.
- `--describe` JSON field schema for `size_bytes` updated from `int` to `int|null` to match the post-fix emitter.
- Path that exists but is a regular file (not a directory) now reports "not a directory" instead of the misleading "path not found", and surfaces `exit_reason: not_a_directory` in the `--json` envelope.
- `--version` output no longer carries the `+gitsha` SourceLink suffix. Users see plain `treex 0.3.0`, matching the suite-wide convention.

### Added
- Library seam `Winix.TreeX.Cli.Run` for orchestration testing without process spawning. Matches sibling-tool pattern (`clip`, `digest`, `url`, `qr`, `whoholds`).
- `--json` pre-walk error envelopes (path_not_found, not_a_directory) now carry an `error` field with the human-readable failure detail (in addition to the machine-readable `exit_reason`).
- `--json` summary envelope now includes a `walk_errors` array enumerating directories and files that could not be read during the walk. Each entry is `{"path": "...", "reason": "..."}`. Always present (empty array on success); non-empty when `exit_reason: "walk_error_partial"`. Closes the JSON-consumer blind spot where the prior envelope reported `exit_reason: walk_error_partial` but didn't enumerate which paths failed.

## [0.2.0] - 2026-04-16

### Added
- Manual page (`man 1 treex`) ships with the package and is installed to `share/man/man1/` by scoop and the native installer.

## [0.1.0] - 2026-04-02

- Initial release.
