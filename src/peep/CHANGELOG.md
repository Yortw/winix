# Changelog

All notable changes to **peep** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

### Changed
- `--json` / `--json-output` envelopes now reach every exit path, including parser errors, missing-command, regex-parse errors, and unexpected runtime exceptions in once-mode. Pre-fix, several validation early-returns and the once-mode catch list emitted bare stderr lines, breaking JSON-aware automation that decided "envelope or not" by inspecting `--json-output`.
- JSON `last_output` field is now trimmed of trailing newlines to match `bash $(cmd)` command-substitution semantics. Internal newlines are preserved unchanged; the captured `PeepResult.Output` stays byte-faithful for stdout / `--exit-on-match` / history-diff consumers.
- Captured child output is normalised to `\n` line endings at the API boundary, so JSON envelopes and `--exit-on-match` regexes behave identically across Windows and POSIX (pre-fix, `dotnet --version` produced `"10.0.200\r\n"` on Windows but `"10.0.200\n"` on Linux).
- `--exit-on-match` and `--exit-on-change` now return exit code 0 for the auto-exit success case, matching the documented "0 = Auto-exit condition met" contract. Pre-fix the child's last exit code (often non-zero on the run that triggered the match) was passed through.
- Once-mode JSON envelope now emits `history_retained: 0` so the field is always present, matching `--describe`'s declared `int|null` schema.

### Fixed
- `peep <command>` no longer crashes with an SR resource-key exception (`InvalidOperationException: InvalidOperation_ConsoleKeyAvailableOnFile`) when stdin is redirected (pipes, `/dev/null`, CI, parent-process `RedirectStandardInput=true`). The redraw loop now probes `Console.IsInputRedirected` once at entry and skips the keyboard branch on every tick when stdin is not a terminal.
- Two related stream-merge bugs fixed: child stdout and stderr are now merged line-atomically rather than in 4096-char chunks (a chunk from one stream could land mid-line of the other, producing output like `"this naCould not execute...me could not be found"`), and the line buffer enforces the 64 MB output cap mid-stream so a child writing without `\n` (binary dumps, unfolded base64, large `curl` payloads) can no longer bypass the OOM bound.
- File-change triggers are no longer silently dropped while a child is running. The previous `Interlocked.CompareExchange` ordering consumed the trigger flag before checking whether a child was already running, so saves during a long `dotnet test` were silently lost.
- ANSI-strip regex now also matches OSC sequences (`ESC ] ... BEL` and `ESC ] ... ESC \`) used by modern shells, fish, oh-my-posh, gcc, and OSC-8 hyperlinks. Pre-fix `--exit-on-match` patterns failed against lines carrying a leading title-set escape, and `--json-output last_output` leaked raw escape bytes into the JSON envelope.
- `RegexMatchTimeoutException` from `--exit-on-match` against a pathological pattern is now caught (treated as non-match) with a one-shot stderr warning per pattern. Pre-fix the timeout was silently swallowed and the user's auto-exit condition never fired with no diagnostic.
- Captured child output is bounded by a 64 MB cap with a single trailing truncation marker; readers continue draining after the cap so the child sees clean EOF rather than wedging on a full pipe. A runaway child can no longer OOM peep itself.
- `GitIgnoreChecker` no longer leaks orphan `git` processes when `git rev-parse` / `check-ignore` hangs (credential helper prompts, network FS, antivirus scanning `.pack` files). Hung calls are killed on timeout, gitignore filtering is process-wide-disabled with a one-shot stderr warning, and subsequent calls short-circuit. `git` subprocesses now also redirect and immediately close stdin so an interactive credential helper can't hold the parent process hostage.
- `peep --once` now respects Ctrl+C (exits 130 with a complete JSON envelope under `--json` / `--json-output`), handles `CommandStreamException`, and has a last-resort catch-all so any unexpected exception escaping `CommandExecutor.RunAsync` produces a typed envelope rather than an unhandled-exception stack trace. `OutOfMemoryException` and `StackOverflowException` deliberately remain uncaught.
- Interactive Ctrl+C handler no longer races against handler-unregister: `cts.Cancel()` is wrapped in `try/catch (ObjectDisposedException)` so a Ctrl+C arriving just before unregister can no longer crash the cancel-key thread.
- `process.StandardInput.Close()` no longer throws `IOException` when a fast-exiting child has already disconnected the pipe. Closing a pipe that's already gone is the success state.
- `Process.Start` exception coverage extended beyond `Win32Exception` to also catch `FileNotFoundException` (.NET 5+ may surface missing executables directly) → `command_not_found` (127) and `InvalidOperationException` / `PlatformNotSupportedException` / `ArgumentException` → `command_not_executable` (126), per the `--describe` contract.
- Alternate-screen-buffer enter / exit now swallows `IOException` and `ObjectDisposedException` from the underlying writer, so an I/O error at teardown can no longer leave the user's terminal stuck in alt-buffer mode with a hidden cursor for the rest of the shell session.
- File-watcher monitor task now has a strictly-weaker last-resort catch with a stderr warning (`file-change monitor crashed; file-change triggering disabled`). Pre-fix an unexpected crash silently disabled file-change triggering and peep kept polling on interval-only with no diagnostic.
- `--version` output no longer carries the `+gitsha` SourceLink suffix the .NET SDK appends by default. Users see plain `peep 0.3.0`, matching the suite-wide convention.

### Internal
- Library seam `SessionHelpers` extracted from `InteractiveSession` and `Program`: `WarnOnceForRegexTimeout`, `TryGetAutoExit`, `ResolveExitCode`, `RequestCancellationSilently`, and `ShouldDispatch` are now testable as named static methods rather than buried inside event-loop lambdas.
- `CommandExecutor.MaxOutputChars` changed from a process-global mutable static test seam (caused parallel-test flakiness and leaked test-overridden caps into user-facing truncation markers) to an optional per-call parameter.
- UTF-8 console adoption via `ConsoleEnv.UseUtf8Streams` so `--describe` em-dashes and any non-ASCII pipe output round-trip cleanly through Windows `cmd.exe`.
- Standard `<PackageTags>` set on the NuGet package so peep appears in nuget.org filtered searches (previously discoverable only by exact-name lookup).
- Platform-skipped tests migrated to `SkippableFact + Skip.IfNot` so non-applicable tests report Skipped rather than Passed on the wrong platform.

## [0.2.0] - 2026-04-16

### Added
- Manual page (`man 1 peep`) ships with the package and is installed to `share/man/man1/` by scoop and the native installer.

## [0.1.0] - 2026-04-02

- Initial release.
