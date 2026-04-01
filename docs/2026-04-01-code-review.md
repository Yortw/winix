# Winix Full Code Review — 2026-04-01

Full codebase review: 236 files, ~20K lines of C# across 14 projects.

**Overall:** The codebase is well-structured and consistent. Thin-console-app architecture is cleanly followed, AOT configuration is correct, shared ShellKit library provides good consistency. The issues below are real but the codebase is in good shape for its maturity.

---

## Critical — Should Fix

### 1. Wargs: child processes leaked on cancellation

- [x] **Fixed** (3a9fb31)

**Files:** `src/Winix.Wargs/JobRunner.cs:427-428, 528`

`ExecuteJobAsync` and `ExecuteJobLineBufferedAsync` await `WaitForExitAsync(cancellationToken)`, but when the token fires, nothing actually **kills** the child process. The managed `Process` handle gets disposed in the `finally` block, but the OS process keeps running indefinitely.

Peep's `CommandExecutor.RunAsync` has the correct pattern with `cancellationToken.Register(() => process.Kill(entireProcessTree: true))` — Wargs needs the same pattern in both methods.

---

### 2. TimeIt: Linux `ru_maxrss` delta can be zero or misleading

- [x] **Fixed** (8b951b8)

**File:** `src/Winix.TimeIt/NativeMetrics.Linux.cs:83`

`RUSAGE_CHILDREN.ru_maxrss` on Linux reports the peak RSS of the *most resource-intensive* waited child, not a per-child accumulator. The baseline-delta approach (`post.ru_maxrss - baseline.ru_maxrss`) can produce zero if the new child used less memory than a previously-waited child. Needs `Math.Max(0, delta)` clamping at minimum, or a different measurement approach.

---

### 3. GitIgnoreFilter: trailing-backslash argument injection on Windows

- [x] **Fixed** (0b532ce)

**File:** `src/Yort.ShellKit/GitIgnoreFilter.cs:75`

`EscapeArg` wraps the path in double-quotes, but a path ending in `\` produces `"foo\"` on the wire — the MSVC C-runtime parser reads the trailing `\"` as an escaped quote, corrupting the argument list. This is the classic Windows trailing-backslash argument injection bug.

**Fix:** Use `ProcessStartInfo.ArgumentList` (available since .NET 5) which handles quoting internally:

```csharp
var psi = new ProcessStartInfo("git")
{
    ArgumentList = { "check-ignore", "-q", "--", relativePath },
    ...
};
```

---

### 4. TreeBuilder: no symlink cycle detection — StackOverflowException

- [x] **Fixed** (8b951b8)

**File:** `src/Winix.TreeX/TreeBuilder.cs:144-174`

`FileWalker` has a `FollowSymlinks` option with a `HashSet<string>` cycle guard. `TreeBuilder` has neither — it always recurses into every directory including symlinks. A self-referential symlink (common on Unix) causes infinite recursion and a `StackOverflowException`.

**Fix:** Add a `HashSet<string>` of resolved real paths (as `FileWalker` does), or skip symlink directories by default and add a `--follow` flag.

---

### 5. `--gitignore` doesn't append trailing `/` for directories

- [x] **Fixed** (3a9fb31)

**Files:** `src/Winix.FileWalk/FileWalker.cs:146-148`, `src/Winix.TreeX/TreeBuilder.cs:139-140`

`git check-ignore` requires a trailing `/` to match directory-specific patterns like `bin/` and `obj/`. Neither `FileWalker` nor `TreeBuilder` appends it, so the most common `.gitignore` entries in .NET projects silently fail to match.

**Fix:**
```csharp
string ignoreQuery = isDirectory ? relativePath + "/" : relativePath;
if (_isIgnored != null && _isIgnored(ignoreQuery))
```

---

## High — Should Fix When Convenient

### 6. ConsoleEnv: ANSI VT processing not enabled on stderr

- [x] **Fixed** (5e8a9ab)

**File:** `src/Yort.ShellKit/ConsoleEnv.cs:20-46`

`EnableAnsiIfNeeded` only calls `SetConsoleMode` on `STD_OUTPUT_HANDLE` (-11). Since the project convention puts coloured summary output on stderr, tools writing ANSI to stderr on older Windows CMD/PowerShell will print literal escape sequences. Need the same `SetConsoleMode` call on `STD_ERROR_HANDLE` (-12).

---

### 7. `ResolveColor()` hardcodes stdout terminal check — wrong for stderr-writing tools

- [x] **Fixed** (5e8a9ab)

**File:** `src/Yort.ShellKit/ParseResult.cs:150`

`ConsoleEnv.IsTerminal(checkStdErr: false)` always checks stdout. When a user pipes stdout (e.g. `squeeze file > out.bin`), stderr may still be a terminal and should get colour, but `ResolveColor()` returns false. Affects squeeze, files, treex, and wargs. The `squeeze/Program.cs:141` workaround for `showStats` shows awareness of this gap.

**Fix:** Add a `ResolveColor(bool checkStdErr = false)` overload, or split into `ResolveColorForStdout()` / `ResolveColorForStderr()`.

---

### 8. Wargs: `--fail-fast` with `--parallel` doesn't cancel in-flight jobs

- [x] **Fixed** (93e6679)

**File:** `src/Winix.Wargs/JobRunner.cs:293-296`

Setting `Volatile.Write(ref aborted, true)` prevents new jobs from starting but does nothing about already-running ones. A user doing `wargs --parallel 8 --fail-fast` expects early termination, but running jobs continue to completion.

**Fix:** Use a linked `CancellationTokenSource` that gets cancelled when `aborted` is set, so in-flight jobs receive cancellation.

---

### 9. `--gitignore` with multiple roots uses only the first root's filter

- [x] **Fixed** (523ad19)

**Files:** `src/files/Program.cs:266`, `src/treex/Program.cs:242`

`GitIgnoreFilter.Create(roots[0])` is shared across all roots. For non-first roots, the relative paths are computed from the wrong working directory, producing silently wrong `git check-ignore` results.

**Fix:** Create one `GitIgnoreFilter` per root, or document that `--gitignore` only applies to the first root and emit a warning for additional roots.

---

### 10. Squeeze: `--stdout` buffers entire result into MemoryStream

- [x] **Fixed** (8756423)

**File:** `src/Winix.Squeeze/FileOperations.cs:236-267`

`ProcessFileToStreamAsync` buffers the full compressed/decompressed output in a `MemoryStream` for byte-counting before copying to stdout. OOM risk on large files. Since the input is a seekable `FileStream`, a counting wrapper stream could avoid the double-buffer.

---

### 11. Squeeze: partial-failure JSON output is two separate objects

- [x] **Fixed** (8756423)

**File:** `src/squeeze/Program.cs:183-213`

When processing multiple files and one fails, stderr gets a `FormatJsonError` object followed by a separate `FormatJson` success envelope — two independent JSON documents with no delimiter. A consumer can't parse this as a single document or as NDJSON.

**Fix:** Include failures in the same JSON envelope with an `errors` array, or adopt NDJSON with one object per line.

---

### 12. Peep: synchronous `git check-ignore` per FileSystemWatcher event

- [x] **Fixed** (bc4e826)

**Files:** `src/Winix.Peep/GitIgnoreChecker.cs:47-75`, `src/Winix.Peep/FileWatcher.cs:186`

`IsMatch` is called synchronously on thread-pool threads for every raw FSW event *before* debouncing. A `git checkout` touching many files fires hundreds of synchronous `git` processes in parallel, risking thread-pool exhaustion.

**Fix:** Cache the `git check-ignore` result per-path with a short TTL or LRU cache, or batch the check, or move the filter check to after the debounce fires.

---

### 13. Peep: `_running` flag not volatile across async continuations

- [x] **Fixed** (16f455f)

**File:** `src/Winix.Peep/InteractiveSession.cs:166, 311, 482, 512`

In a console app without a `SynchronizationContext`, `await` continuations can resume on different thread-pool threads. `_running` is read and written without `Volatile.Read`/`Volatile.Write`, risking stale cache reads.

**Fix:** Use `volatile bool _running` or `Volatile.Read`/`Volatile.Write` at all access points.

---

### 14. TimeIt: `Win32Exception` fall-through maps all errors to "command not found"

- [x] **Fixed** (16f455f)

**File:** `src/Winix.TimeIt/CommandRunner.cs:86-93`

Error codes 5/13 correctly map to `CommandNotExecutableException`. Everything else — including `ERROR_BAD_EXE_FORMAT` (wrong architecture), `ERROR_NOT_ENOUGH_MEMORY`, etc. — falls through to `CommandNotFoundException`, which is misleading.

**Fix:** Rethrow the original `Win32Exception` for unrecognised error codes, or map to a general `CommandException` with the original message.

---

### 15. TimeIt: JSON errors go to stdout when `--stdout` is active

- [x] **Fixed** (16f455f)

**File:** `src/timeit/Program.cs:74-79, 86-91`

When `--stdout --json` is combined, error JSON goes to `writer` (stdout). Every other tool always writes errors to stderr. The intent of `--stdout` is to redirect the timing *summary*, not error output.

**Fix:** Change exception catch blocks to always use `Console.Error` for JSON error output.

---

### 16. Peep: magic exit code literals instead of constants

- [x] **Fixed** (16f455f)

**File:** `src/peep/Program.cs:186, 198`

Hardcoded `127` and `126` instead of `ExitCode.NotFound` / `ExitCode.NotExecutable`. Only place in the entire codebase that uses the magic numbers directly.

---

## Medium

### 17. GitIgnoreFilter: two-pipe deadlock in `CheckBatchChunk`

- [x] **Fixed** (0b532ce)

**File:** `src/Yort.ShellKit/GitIgnoreFilter.cs:140-144`

stdout and stderr are read sequentially. If `git` writes enough to stderr to fill the OS pipe buffer before stdout is drained, both block. The classic two-pipe deadlock.

**Fix:** Read stdout and stderr concurrently using `Task.Run` or `BeginRead`/`BeginErrorReadLine`.

---

### 18. GitIgnoreFilter: batch output path matching may fail on separator normalization

- [x] **Fixed** (0b532ce)

**File:** `src/Yort.ShellKit/GitIgnoreFilter.cs:146-154`

Verbatim string comparison after `Trim()`. Git can normalize path separators in output. Also, filenames with embedded newlines would corrupt the line-based protocol.

**Fix:** Use `-z` (NUL-delimited I/O) which also eliminates the newline-in-filename risk:
```csharp
var psi = new ProcessStartInfo("git", "check-ignore -z --stdin") { ... };
```

---

### 19. CommandLineParser: `BuildLookups` not thread-safe

- [ ] **Fix**

**File:** `src/Yort.ShellKit/CommandLineParser.cs:437-479`

Lazy init without synchronisation. Low practical risk since parsers are used single-threaded, but the pattern invites future misuse.

**Fix:** Call `BuildLookups()` eagerly at the end of the constructor, removing the lazy guard entirely.

---

### 20. Wargs: `--line-buffered` + `--parallel > 1` not validated

- [ ] **Fix**

**File:** `src/wargs/Program.cs:117-121`

Line-buffered mode inherits stdio directly. Parallel line-buffered jobs interleave output at the character level, which is almost never what the user wants.

**Fix:** Reject the combination with an error message, similar to how `--confirm` + `--parallel > 1` is already rejected.

---

### 21. Peep: permission errors on subsequent runs silently show stale output

- [ ] **Fix**

**File:** `src/Winix.Peep/InteractiveSession.cs:534-548`

`CommandNotExecutableException` on a re-run returns null; screen keeps showing last successful output with no visible indication the command is now failing. The error goes to `Console.Error` which is behind the alternate screen buffer.

**Fix:** Re-render the screen to show an error state, or overlay an error indicator in the status bar.

---

### 22. Duplicate exception types across TimeIt and Peep

- [ ] **Fix**

**Files:** `src/Winix.TimeIt/CommandRunner.cs:9-40`, `src/Winix.Peep/CommandNotFoundException.cs`, `src/Winix.Peep/CommandNotExecutableException.cs`

Identical purpose and nearly identical implementation. These belong in `Yort.ShellKit` as shared types alongside `ExitCode`.

---

### 23. Squeeze: exit code 2 deviation undocumented at call site

- [ ] **Fix**

**File:** `src/squeeze/Program.cs:38, 50-52`

Uses `UsageErrorCode(2)` for gzip compatibility instead of 125 (POSIX). Intentional, but the exit-code table has no comment explaining why it deviates from the project standard.

**Fix:** Add a one-line comment at the exit-code table: `// gzip-compatible: usage error is 2, not POSIX 125`.

---

### 24. CommandLineParser: `\r\n` in section body produces trailing `\r`

- [ ] **Fix**

**File:** `src/Yort.ShellKit/CommandLineParser.cs:558`

`GenerateHelp` splits on `\n` only; `TrimStart()` doesn't strip `\r`. If a caller passes Windows line-ending body text, each non-empty line carries a trailing `\r`.

**Fix:** Use `line.Trim()` instead of `line.TrimStart()`, or split on `new[] { "\r\n", "\n" }`.

---

### 25. DisplayFormat: negative byte counts silently format

- [ ] **Fix**

**File:** `src/Yort.ShellKit/DisplayFormat.cs:20`

`FormatBytes(-1)` returns `"-1 B"` — semantically invalid with no guard or documentation.

**Fix:** Guard with `ArgumentOutOfRangeException` on `bytes < 0`, or document the negative-input behaviour explicitly.

---

### 26. TreeBuilder: sort always case-insensitive regardless of `--case-sensitive` flag

- [ ] **Fix**

**File:** `src/Winix.TreeX/TreeBuilder.cs:279`

`StringComparison.OrdinalIgnoreCase` is hardcoded. On Linux, users expect case-sensitive sort when they ask for it.

**Fix:**
```csharp
_ => string.Compare(a.Name, b.Name,
         _options.CaseInsensitive
             ? StringComparison.OrdinalIgnoreCase
             : StringComparison.Ordinal)
```

---

## Build / CI / Packaging

### 27. Scoop manifests broken for wargs, files, treex

- [ ] **Fix**

**Files:** `bucket/wargs.json`, `bucket/files.json`, `bucket/treex.json`

Placeholder `v0.0.0` URLs and `0000...` hashes. Any Scoop install attempt for these tools will fail. Version fields are also inconsistent across the three files.

**Fix:** These should be updated by the release pipeline on first release. Until then, either remove them from the bucket or add a note. Also ensure `winix.json`'s `bin` array doesn't reference downloads that don't exist yet.

---

### 28. `peep.json` hash is 63 hex chars (invalid SHA-256)

- [ ] **Fix**

**File:** `bucket/peep.json:9`

SHA-256 requires 64 hex chars. This hash is 63 chars. Scoop will reject with hash verification error.

**Fix:** Verify the correct SHA-256 of `peep-win-x64.zip` from the `v0.1.0-preview.4` release and update.

---

### 29. macOS x64 cross-compilation risk

- [ ] **Fix**

**File:** `.github/workflows/release.yml:115-116`

Both `osx-x64` and `osx-arm64` use `macos-latest` (currently arm64). The x64 target is cross-compiling on arm64 hardware. If GitHub ever changes `macos-latest` back to x64, `osx-arm64` would silently become a cross-compile.

**Fix:** Use explicit runner images: `macos-13` for x64, `macos-14`/`macos-latest` for arm64. Or add a comment documenting the dependency.

---

### 30. `workflow_dispatch` version input not validated

- [ ] **Fix**

**File:** `.github/workflows/release.yml:26-33`

A typo like `v0.2.0` (leading `v`) produces `vv0.2.0` tag and NuGet packages. Spaces or invalid semver would produce malformed artifacts that can't be deleted from NuGet.

**Fix:** Add a regex validation step:
```bash
if ! echo "$VERSION" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$'; then
  echo "Invalid version format: $VERSION" && exit 1
fi
```

---

### 31. PackAsTool + PublishAot: NuGet is JIT, releases are AOT

- [ ] **Fix/Document**

**Files:** All console `.csproj` files

`dotnet tool install Winix.TimeIt` installs the JIT (framework-dependent) version, not the AOT native binary. This is not wrong, but it's inconsistent with the project's AOT-first positioning and should be documented.

**Fix:** Document the dual-mode distribution (NuGet = JIT tool, Scoop/GitHub = AOT native) in the README/install docs.

---

### 32. `RuntimeFrameworkVersion=10.0.0` exact pin

- [ ] **Fix**

**File:** `Directory.Build.props:10`

Exact pin to `10.0.0` is fragile if CI runner has only a later patch version. `global.json` SDK pin is sufficient.

**Fix:** Remove `RuntimeFrameworkVersion` from `Directory.Build.props` unless there's a specific reason it's needed.

---

### 33. Scoop bucket push has no conflict handling

- [ ] **Fix**

**File:** `.github/workflows/release.yml:253-302`

Direct push to `main` with no conflict handling. Concurrent releases would fail with non-fast-forward error, leaving manifests at inconsistent versions.

**Fix:** Add `git pull --rebase` before pushing, or use `--force-with-lease`.

---

### 34. CI workflow missing explicit `permissions` block

- [ ] **Fix**

**File:** `.github/workflows/ci.yml`

No `permissions` block. Private repo defaults to write-all, which is unnecessarily broad for a build-and-test workflow.

**Fix:** Add:
```yaml
permissions:
  contents: read
```

---

## Low / Documentation Gaps

### 35. IntervalScheduler is unused dead code

- [ ] **Fix**

**File:** `src/Winix.Peep/IntervalScheduler.cs`

Fully built and tested but has no caller. `InteractiveSession` uses manual `DateTime.UtcNow` polling instead. Ships in the library's public surface, inflates the API.

**Fix:** Either use it in `InteractiveSession`, or remove it (and its tests) if the manual approach is the permanent design.

---

### 36. `CheckBatch` has zero test coverage

- [ ] **Fix**

**File:** `tests/Yort.ShellKit.Tests/GitIgnoreFilterTests.cs`

`GitIgnoreFilter.CheckBatch` is a public method with non-trivial chunking logic (findings 17/18 live there), but it has no tests at all. No happy path, no chunk-boundary case, no empty-input shortcut.

---

### 37. `GetTerminalWidth`/`GetTerminalHeight` can return 0

- [ ] **Fix**

**File:** `src/Yort.ShellKit/ConsoleEnv.cs:124-133`

`Console.WindowWidth` returns `0` on some Linux CI/redirect scenarios (rather than throwing). The `catch` fallback to `80` never fires in that case, so callers can receive `0` and produce divide-by-zero or zero-column layout.

**Fix:** `return value > 0 ? value : 80` after the `Console.WindowWidth` call.

---

### 38. Missing XML `<param>` docs on public records

- [ ] **Fix**

**Files:** `src/Winix.FileWalk/FileWalkerOptions.cs`, `src/Winix.Peep/SessionConfig.cs`

`FileWalkerOptions` (14 params) and `SessionConfig` (19 params) are public records with no `<param>` documentation. Every other public record in the codebase has param docs.

---

### 39. Missing XML docs on CommandLineParser internal types

- [ ] **Fix**

**File:** `src/Yort.ShellKit/CommandLineParser.cs:786-798`

`FlagDef`, `OptionType`, `OptionDef`, `ListOptionDef`, and `AliasDef` are `internal` types visible to tests via `InternalsVisibleTo`. Per project guidelines, all public/protected/internal members should have XML doc comments.

---

### 40. CommandLineParser: post-parse mutation silently uses stale lookups

- [ ] **Fix**

**File:** `src/Yort.ShellKit/CommandLineParser.cs:436-439`

If a caller registers additional flags after the first `Parse()` call, `BuildLookups` returns immediately (null-guard), and new flags are silently ignored in subsequent parses with no error.

**Fix:** Track a `_parsed` flag and throw `InvalidOperationException` on mutation attempts after parsing. Or document that post-parse registration is unsupported.

---

### 41. Missing tests for exit code 126 and non-standard Win32Exception paths

- [ ] **Fix**

**File:** `tests/Winix.TimeIt.Tests/CommandRunnerTests.cs`

No test for the permission-denied path (exit code 126) or the `Win32Exception` fall-through for non-5/13 error codes (finding 14).

---

## What's Working Well

These patterns are worth preserving:

- **Architecture:** all 6 tools follow the thin-console-app / rich-library split cleanly
- **AOT configuration:** correct `PublishAot`, `IsTrimmable`, `IsAotCompatible`, `InvariantGlobalization`, `LibraryImport` usage, struct layouts per-RID
- **No `Environment.Exit()` anywhere** — all tools return exit codes from `Main()`
- **`NO_COLOR` compliance** consistently implemented via `ResolveColor()`
- **`ConsoleEnv.EnableAnsiIfNeeded()`** called at startup in every tool
- **stderr/stdout separation** correct across all tools
- **Nullable reference types** enabled project-wide with warnings-as-errors
- **Argument parsing** consistent via the same `CommandLineParser` fluent builder
- **Test coverage** solid — 416+ tests covering formatting, edge cases, integration
- **`CommandLineParser`** is a well-designed, feature-rich shared component
