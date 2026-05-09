# Changelog

All notable changes to **squeeze** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

### Changed (BREAKING)
- Multi-member gzip detection dropped. Pre-fix some incompressible binary inputs triggered false multi-member detection on decode. The detection logic is now removed; the trade-off is a louder false-positive footprint on legitimately-multi-member archives in exchange for never silently corrupting single-member output. Single-member gzip — the overwhelming common case — is unaffected.

### Fixed
- Truncated gzip decode no longer silently ships garbage. ISIZE field validation now rejects malformed input with a clear error message instead of completing exit 0 with a partial / corrupt stdout. This was the headline silent-corruption defect surfaced by tier-2 review.
- Framework SR resource keys (e.g. `Arg_ParamName_Name`) no longer leak into user-facing error output under `InvariantGlobalization=true`. Tool-supplied English messages now used throughout.
- IOException on read/write paths now caught and surfaced with context rather than escaping as a stack trace.
- `--version` output no longer carries the `+gitsha` SourceLink suffix the .NET SDK appends by default. Users see plain `squeeze 0.3.0`, matching the convention across the rest of the suite.

### Added
- Library seam `Winix.Squeeze.Cli.Run` for orchestration testing without process spawning. Matches the suite-wide pattern.

### Internal
- UTF-8 console adoption via `ConsoleEnv.UseUtf8Streams` so multi-byte filenames round-trip correctly on Windows.
- Standard `<PackageTags>` set on the NuGet package so it's discoverable via tag filters on nuget.org.

## [0.2.0] - 2026-04-16

### Added
- Manual page (`man 1 squeeze`) ships with the package and is installed to `share/man/man1/` by scoop and the native installer.

## [0.1.0] - 2026-04-02

- Initial release.
