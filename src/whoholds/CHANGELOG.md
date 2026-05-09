# Changelog

All notable changes to **whoholds** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

### Changed
- `--json` output now goes to **stdout** (was stderr) per suite convention. Pipe-friendly for `jq`; matches `man`, `winix`, `treex`, `files`.

### Fixed
- `lsof` rows whose command name contains spaces (multi-word names like `Google Chrome`) are no longer silently dropped. Pre-fix the fixed-column-index parser truncated at the first whitespace boundary, hiding a class of legitimate locks from the result list.
- Backend API failures (e.g. `lsof` exit non-zero, RM probe error) now route to **exit 1** with a stderr diagnostic. Pre-fix backend failures resulted in a silent empty result list with exit 0, indistinguishable from "no locks found".
- File-not-found path returns **exit 1** (was 125) per the documented contract — file-not-found is a target-state error, not a usage error.
- Stuck child stream reads (`lsof` hanging on a stale fd, RM probe wedged on a hung process) now surface a categorised stderr diagnostic with a timeout warning rather than silent hang.
- Windows `RM_UNIQUE_PROCESS` struct layout corrected — process names ≥ 64 chars no longer truncated mid-string.
- Restart Manager probe now retries on the eventual-consistency edge where a freshly-opened lock isn't yet visible to RM.
- `--version` output no longer carries the `+gitsha` SourceLink suffix.

### Added
- Library seam `Winix.WhoHolds.Cli.Run` for orchestration testing without process spawning.

### Documentation
- README and man page corrected: JSON example shape and field list now match the actual emitter.
- Elevation-warning text unified across surfaces.
- Wargs pipe example unified to `taskkill /PID {} /F` form for Windows users.

### Internal
- UTF-8 console adoption via `ConsoleEnv.UseUtf8Streams`.
- Standard `<PackageTags>` set on the NuGet package.
- ProcessRunner test seam added so backend integration tests can pin shell-output parsing without spawning real `lsof` / RM calls.
- Platform-skipped tests migrated to `SkippableFact + Skip.IfNot`.

## [0.2.0] - 2026-04-16

- Initial release.
