# Changelog

All notable changes to **timeit** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

### Fixed
- Empty-command argument (`timeit ""` or just `timeit`) no longer leaks the `FileNameMissing` SR resource key under `InvariantGlobalization=true`. A proper user-facing English message now surfaces instead of the framework SR key.
- Empty-command path now routes the usage-error message to **stderr** (was stdout), so `timeit "" 2>/dev/null` no longer silently swallows the diagnostic.
- Bad-EXE invocation (Windows: `.cmd` / `.bat` files; POSIX: missing executable bit) now platform-gated correctly. The cross-platform behaviour gap that produced `IOException` on Windows but a clean error on POSIX is closed.
- Exit-code semantics corrected per documented contract — usage errors at 125, command-not-executable at 126, command-not-found at 127.
- `--version` output no longer carries the `+gitsha` SourceLink suffix. Users see plain `timeit 0.3.0`, matching the suite convention.

### Added
- Library seam `Winix.TimeIt.Cli.Run` for orchestration testing without process spawning.
- Documentation: README and man page now spell out exit-code semantics explicitly.

### Internal
- UTF-8 console adoption via `ConsoleEnv.UseUtf8Streams` so multi-byte argv round-trips correctly on Windows.
- Standard `<PackageTags>` set on the NuGet package.
- Platform-skipped tests migrated to `SkippableFact + Skip.IfNot` so per-OS branches no longer report as Passed on the wrong platform (CI false-positive class).

## [0.2.0] - 2026-04-16

### Added
- Manual page (`man 1 timeit`) ships with the package and is installed to `share/man/man1/` by scoop and the native installer.

## [0.1.0] - 2026-04-02

- Initial release.
