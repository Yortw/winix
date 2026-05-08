# Changelog

All notable changes to **less** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.4.0] - 2026-05-09

### Changed (BREAKING)
- Multi-file paging removed. Pre-fix the binary silently overwrote `filePath` with each subsequent positional, opening only the LAST one. README and man both claimed "Multiple files are paged in sequence." Now `less file1 file2` exits with usage error 2 and a clear message. For concatenated paging, use `cat file1 file2 | less`. True multi-file paging with `:n` / `:p` navigation is tracked for v0.5+.
- Usage errors now exit with **2** (POSIX-traditional, matches GNU `less`) instead of ShellKit's suite-default 125. Deliberate suite divergence per `feedback_match_established_tool_conventions.md` since less is a POSIX-replacement.

### Fixed
- `LESS=NiR less file.txt` (or any `LESS` value without `F`) no longer crashes with `IOException: The handle is invalid` on redirected stdout. Pager now detects non-tty up front via `Pager.SelectDumpStrategy` and dumps content directly to stdout when not a terminal — matches GNU `less` behaviour. Plus belt-and-braces try/catch around the pager loop catches mid-loop terminal failures and falls back to a viewport-preserving direct dump.
- `NO_COLOR` env var, `--color`, and `--no-color` flags are now honoured. Pre-fix all three were silently ignored — ANSI passthrough was unconditional. Now `result.ResolveColor()` drives `LessOptions.StripAnsi`, which strips ANSI escapes from rendered output and the dump path. Matches Winix suite-wide `NO_COLOR` policy and aligns with ripgrep / fd / bat (diverges from GNU less which doesn't honour `NO_COLOR`).
- Bare `-` argument is now accepted as the POSIX explicit-stdin marker. Pre-fix ShellKit consumed `-` as an unknown short option and failed with exit 125. `less -` and `less - file.txt` both work; explicit `-` wins over the file argument per POSIX precedence.
- Directory path argument now reports "Is a directory" via `IOException` instead of the misleading "File not found". Same shape as treex/files F4.
- File-load now catches `IOException` and `UnauthorizedAccessException` in addition to the previously-only-caught `FileNotFoundException`. Pre-fix a path-too-long, locked-for-exclusive-write file, or permission-denied target crashed with a stack trace. `UnauthorizedAccessException` emits a tool-supplied English message to avoid leaking SR resource keys under InvariantGlobalization.
- `LESS=` (empty) now correctly disables all defaults. Pre-fix code conflated null and empty via `string.IsNullOrEmpty`, so the documented "set LESS= explicitly to empty to disable defaults" contract was unreachable.
- Pager-Screen-ReattachUnix cleanup-class triangle (round-1 fresh-eyes 2026-05-09): three cooperative silent-failure modes around console-handle lifecycle closed in one fix. Pager.Run now catches both `IOException` AND `InvalidOperationException` (the latter fires when `Console.ReadKey` runs after a failed reattach — common on `git diff | less +F`). `Screen.Dispose` guards each terminal write against `IOException` so unwind doesn't compound the original failure. `ConsoleInput.ReattachUnix` narrows from a bare `catch { }` to typed catches with a one-shot stderr diagnostic. The crash-fallback `DumpFromViewport` preserves the user's current viewport position rather than re-emitting content scrolled past.

### Added
- Library seam `Winix.Less.Cli.Run` for orchestration testing without process spawning or entering the interactive Pager.Run loop. Matches sibling-tool pattern (`clip`, `digest`, `url`, `qr`, `whoholds`, `treex`, `files`).
- `Pager.DumpFromViewport(lines, startIndex)` internal helper for the crash-fallback path; `DumpAllLines` delegates to `DumpFromViewport(lines, 0)` for the F1 small-content path.

## [0.2.0] - 2026-04-16

- Initial release.
