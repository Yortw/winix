# files — Cross-Platform File Finder

**Date:** 2026-03-31
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`files` is a cross-platform `find` replacement with glob and regex patterns, date/size/type predicates, clean output for piping, and structured JSON output. Built on a shared `Winix.FileWalk` library that `treex` will also consume.

**Why not just use `find`?** Windows has no native `find` equivalent (the Windows `find` command searches file content, not names). MSYS2/Git Bash `find` works but has path-mangling issues. Even on Unix, `find`'s expression-based syntax (`-name`, `-exec`, `-mtime +7`) is hostile to both humans and AI agents. `files` uses flags and values like every other Winix tool.

**Why not `fd`?** fd is excellent, but it defaults to hiding `.gitignore`'d and hidden files (dev-centric), uses regex by default, and doesn't have `--json`/`--ndjson`/`--describe` for machine consumption. `files` finds everything by default and supports both glob and regex.

---

## Project Structure

```
src/Yort.ShellKit/          ← + GitIgnoreFilter (new)
src/Winix.FileWalk/         ← NEW: shared walking engine
src/Winix.Files/            ← NEW: output formatting
src/files/                  ← NEW: thin console app
tests/Winix.FileWalk.Tests/ ← NEW
tests/Winix.Files.Tests/    ← NEW
```

Follows standard Winix conventions: library does all work, console app is thin, ShellKit provides arg parsing and terminal detection.

---

## Data Flow

```
paths (args or cwd)
  → FileWalker (Winix.FileWalk)
    → enumerates directory tree, yields FileEntry records
    → applies predicates: glob, regex, type, size, date, depth
    → optionally filters via GitIgnoreFilter (ShellKit)
  → IEnumerable<FileEntry> stream
    → Formatting (Winix.Files)
      → one-path-per-line / long / null-delimited / JSON / NDJSON
  → stdout
```

---

## Components

### FileEntry (Winix.FileWalk)

Immutable record yielded per result:

```csharp
public sealed record FileEntry(
    string Path,              // relative or absolute (caller decides)
    string Name,              // filename only
    FileEntryType Type,       // File, Directory, Symlink
    long SizeBytes,           // -1 if not available (e.g. directory)
    DateTimeOffset Modified,
    int Depth,                // relative to search root
    bool? IsText);            // null unless --text/--binary active; true = text, false = binary
```

### FileEntryType (Winix.FileWalk)

```csharp
public enum FileEntryType { File, Directory, Symlink }
```

### FileWalkerOptions (Winix.FileWalk)

Immutable config record:

```csharp
public sealed record FileWalkerOptions(
    IReadOnlyList<string> GlobPatterns,      // --glob and --ext (OR within, AND with regex)
    IReadOnlyList<string> RegexPatterns,     // --regex (OR within, AND with glob)
    FileEntryType? TypeFilter,               // --type f/d/l
    bool? TextOnly,                          // --text (true), --binary (false), null = no filter
    long? MinSize,                           // --min-size
    long? MaxSize,                           // --max-size
    DateTimeOffset? NewerThan,               // --newer
    DateTimeOffset? OlderThan,               // --older
    int? MaxDepth,                           // --max-depth
    bool IncludeHidden,                      // default true, --no-hidden flips
    bool FollowSymlinks,                     // --follow
    bool UseGitIgnore,                       // --gitignore
    bool AbsolutePaths,                      // --absolute
    bool CaseInsensitive);                   // platform default, overridden by --ignore-case/--case-sensitive
```

### FileWalker (Winix.FileWalk)

The walking engine:

```csharp
public sealed class FileWalker
{
    public FileWalker(FileWalkerOptions options, GitIgnoreFilter? ignoreFilter = null);
    public IEnumerable<FileEntry> Walk(IReadOnlyList<string> roots);
}
```

Yields `FileEntry` records lazily via `yield return`. No buffering the entire tree. Predicates applied during walk (not post-filter) so we can skip entire subtrees when depth/gitignore/hidden rules exclude them.

### Size and Duration Parsing (Winix.FileWalk)

**Size strings**: `100`, `100k`, `100K`, `10M`, `1G` — parsed to bytes. Case-insensitive suffixes. `k` = 1024 (binary), matching common CLI convention (du, find, fd).

**Duration strings**: `30s`, `5m`, `1h`, `7d`, `2w` — parsed to `TimeSpan`, subtracted from `DateTimeOffset.Now` to produce a cutoff timestamp.

Static parsing methods on dedicated types (not extension methods — AOT-friendly, testable):

```csharp
public static class SizeParser
{
    public static long Parse(string value);  // throws FormatException
    public static bool TryParse(string value, out long bytes);
}

public static class DurationParser
{
    public static TimeSpan Parse(string value);
    public static bool TryParse(string value, out TimeSpan duration);
}
```

### GitIgnoreFilter (ShellKit)

Wraps `git check-ignore` for reliability — handles nested `.gitignore`, `.git/info/exclude`, global gitignore (`core.excludesFile`), and all pattern priority rules correctly.

```csharp
public sealed class GitIgnoreFilter : IDisposable
{
    /// <summary>
    /// Creates a filter rooted at the given path. Returns null if not in a git repo
    /// (git not on PATH or path is not inside a git working tree).
    /// </summary>
    public static GitIgnoreFilter? Create(string rootPath);

    /// <summary>
    /// Checks whether a path is ignored by gitignore rules.
    /// </summary>
    public bool IsIgnored(string relativePath);
}
```

**Implementation:** Starts a long-running `git check-ignore --stdin -z` process. Writes paths to stdin, reads results from stdout. One process per filter instance, amortising startup cost across many checks. `IDisposable` to clean up the child process.

**Batch optimisation:** The `--stdin` mode accepts multiple paths, avoiding per-file process spawning. The filter can accept paths one at a time (blocking read) or in batches.

**Fallback:** If `git` is not on PATH or the directory is not a git repo, `Create()` returns `null`. The walker skips gitignore filtering — no error, no warning. The `--gitignore` flag in the console app warns if the filter couldn't be created ("--gitignore specified but git not found on PATH").

---

## CLI Flags

| Flag | Short | Value | Description |
|------|-------|-------|-------------|
| `--glob` | `-g` | PATTERN | Match filenames against glob (repeatable, OR) |
| `--regex` | `-e` | PATTERN | Match filenames against regex (repeatable, OR) |
| `--ext` | | EXT | Match file extension (e.g. `cs`, `json`; repeatable, OR; shorthand for `--glob '*.<ext>'`) |
| `--type` | `-t` | TYPE | Filter by type: `f` (file), `d` (directory), `l` (symlink) |
| `--text` | | | Only text files (no null bytes in first 8KB) |
| `--binary` | | | Only binary files (has null bytes in first 8KB) |
| `--min-size` | | SIZE | Minimum file size (e.g. `100k`, `10M`, `1G`) |
| `--max-size` | | SIZE | Maximum file size (e.g. `100k`, `10M`, `1G`) |
| `--newer` | | DURATION | Modified within duration (e.g. `1h`, `30m`, `7d`) |
| `--older` | | DURATION | Modified before duration |
| `--max-depth` | `-d` | N | Maximum directory depth |
| `--follow` | `-L` | | Follow symlinks (default: don't follow) |
| `--absolute` | | | Output absolute paths (default: relative) |
| `--no-hidden` | | | Skip hidden/dot files and directories |
| `--gitignore` | | | Respect `.gitignore` (requires git on PATH) |
| `--ignore-case` | `-i` | | Case-insensitive pattern matching (overrides platform default) |
| `--case-sensitive` | | | Case-sensitive pattern matching (overrides platform default) |
| `--long` | `-l` | | Tab-delimited columns: path, size, modified, type |
| `--print0` | `-0` | | Null-delimited output (for `wargs -0`) |
| `--json` | | | JSON summary to stderr |
| `--ndjson` | | | Streaming NDJSON (one JSON per file, to stdout) |
| `--help` | `-h` | | Show help |
| `--version` | | | Show version |
| `--describe` | | | Structured metadata for AI agents |
| `--color` | | | Force colour on |
| `--no-color` | | | Force colour off |

**Positional args:** Paths to search. Defaults to `.` (cwd) if none provided. Multiple paths supported.

**No `--compat` flag.** `find`'s expression syntax (`-name`, `-exec`, `-mtime`) is so different that compatibility flags would be misleading.

### Pattern Interaction

Glob and regex filters are complementary, not mutually exclusive:

- Multiple `--glob` patterns: OR (match any)
- Multiple `--regex` patterns: OR (match any)
- Glob + regex together: AND (must match at least one glob AND at least one regex)

This allows `--glob '*.cs' --regex 'test_\d+'` — "C# files whose names match `test_` followed by digits."

Both match against the **filename only** (not the full path). This matches the common case and avoids confusion with path separators in patterns.

### Extension Shorthand

`--ext cs` is sugar for `--glob '*.cs'`. Repeatable with OR logic, like `--glob`. Interacts with `--glob` and `--regex` via the same AND rules (extension matches are glob matches internally).

The extension should be specified without a dot: `--ext cs`, not `--ext .cs`. If someone passes `--ext .cs`, strip the leading dot and warn on stderr.

### Case Sensitivity

Pattern matching (glob, regex, and ext) respects the platform's filesystem case sensitivity by default:

| Platform | Default | Filesystem |
|----------|---------|------------|
| Windows | Case-insensitive | NTFS is case-preserving, case-insensitive |
| macOS | Case-insensitive | APFS defaults to case-insensitive |
| Linux | Case-sensitive | ext4/xfs are case-sensitive |

Override with `--ignore-case` / `--case-sensitive` when the default doesn't match your needs (e.g. case-sensitive search on a case-sensitive macOS volume, or case-insensitive search on Linux for files copied from Windows).

Implementation: detect platform at startup, apply `StringComparison.OrdinalIgnoreCase` (or `RegexOptions.IgnoreCase`) on Windows/macOS, `StringComparison.Ordinal` on Linux. Override flags flip this.

### Text vs Binary Detection

`--text` and `--binary` filter files based on content heuristic: read the first 8KB and check for null bytes. This is the same method git uses (`git diff` treats files with null bytes as binary). Files only — directories and symlinks are skipped by these filters (they don't have content to inspect).

`--text` and `--binary` are mutually exclusive (exit 125 if both provided). They only apply to files — if `--type d` and `--text` are combined, exit 125 (contradictory).

**Performance note:** This reads 8KB per candidate file. For large result sets this adds I/O overhead versus filename-only predicates. Applied as a late filter — after glob/regex/type/size/date predicates have narrowed the set, so the I/O cost is proportional to matches, not to the entire tree.

The `FileEntry` record gains an optional `IsText` field populated only when `--text` or `--binary` is active (avoids reading file content when not needed). This field is also included in NDJSON and `--long` output when populated.

### Default Behaviour Summary

| Behaviour | Default | Override |
|-----------|---------|----------|
| Hidden files | Included | `--no-hidden` to skip |
| .gitignore | Not respected | `--gitignore` to enable |
| Symlinks | Not followed | `--follow` to follow |
| Paths | Relative | `--absolute` for absolute |
| Case sensitivity | Platform-aware (insensitive on Windows/macOS, sensitive on Linux) | `--ignore-case` / `--case-sensitive` |
| Output | One path per line | `--long`, `--print0`, `--ndjson`, `--json` |
| Colour | Auto-detect terminal | `--color` / `--no-color` |

---

## Output Formats

### Default (paths)

One path per line, relative to search root. Colour when terminal detected — directories in blue, symlinks in cyan.

```
src/Winix.Files/Walker.cs
src/Winix.Files/Formatting.cs
src/files/Program.cs
```

### Long (`--long` / `-l`)

Tab-delimited columns: path, size (bytes with commas), modified (local time, no seconds), type.

```
src/Winix.Files/Walker.cs       2,340   2026-03-31 14:22   file
src/Winix.Files/Formatting.cs   1,870   2026-03-31 09:15   file
src/files/Program.cs              640   2026-03-31 14:30   file
```

Tab-delimited so `cut -f2` etc. works for quick extraction.

### Null-delimited (`--print0` / `-0`)

Paths separated by `\0`. Composes with `wargs -0`.

### NDJSON (`--ndjson`)

One JSON object per file to **stdout** (this is a data-producing tool — the NDJSON stream IS the primary output):

```json
{"tool":"files","version":"0.2.0","exit_code":0,"exit_reason":"success","path":"src/Winix.Files/Walker.cs","name":"Walker.cs","type":"file","size_bytes":2340,"modified":"2026-03-31T14:22:00+13:00","depth":2,"is_text":true}
```

Standard fields (`tool`, `version`, `exit_code`, `exit_reason`) are included on every line per CLI conventions — each NDJSON line is self-contained. `is_text` is present only when `--text` or `--binary` is active (null otherwise, omitted from JSON).

### JSON summary (`--json`)

Complete JSON object after walk finishes, to **stderr** (summary alongside the path output on stdout):

```json
{"tool":"files","version":"0.2.0","exit_code":0,"exit_reason":"success","count":42,"searched_roots":["src"]}
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success (found zero or more files — zero matches is success) |
| 1 | Runtime error (permission denied on root path, invalid path) |
| 125 | Usage error (bad flags, invalid pattern) |

---

## Error Handling

**Permission denied on individual files/directories:** Log warning to stderr, continue walking. Don't abort because one directory is unreadable.

**Permission denied on root path:** Exit 1. If the user asked to search a directory they can't read at all, that's a meaningful error.

**Non-existent search root:** If multiple roots given and some exist, search the valid ones and warn about invalid ones on stderr. If all roots are invalid, exit 1.

**Symlink cycles:** Detected and skipped with stderr warning (only when `--follow` is used). Track visited real paths to detect cycles.

**Long paths (Windows):** .NET handles `\\?\` long path prefix automatically on modern Windows. No special handling needed.

**Invalid glob/regex pattern:** Exit 125 (usage error) with clear message. Caught at parse time before walking starts.

**Encoding:** Filenames are platform-native strings. .NET's `Directory.EnumerateFileSystemEntries` abstracts this. Invalid-encoding filenames on Linux may produce replacement characters; no special handling for v1.

---

## `--describe` Metadata

`files` is the proving ground for the AI discoverability feature defined in the [AI discoverability design](2026-03-31-ai-discoverability-design.md). The console app registers all metadata via ShellKit's fluent builder:

```csharp
parser.Platform("cross-platform",
          replaces: ["find"],
          valueOnWindows: "No native find equivalent; fills a major gap",
          valueOnUnix: "Cleaner flag syntax, --json output, composes with wargs")
      .StdinDescription("Not used")
      .StdoutDescription("One file path per line (default). Null-delimited with --print0. NDJSON with --ndjson.")
      .StderrDescription("Warnings, errors, and --json summary output.")
      .Example("files src --glob '*.cs'", "Find all C# source files under src/")
      .Example("files . --newer 1h --type f", "Files modified in the last hour")
      .Example("files . --glob '*.log' | wargs rm", "Delete all log files (compose with wargs)")
      .Example("files . --long --glob '*.cs'", "List C# files with size and date")
      .Example("files . --ext cs", "Find all C# files (shorthand for --glob '*.cs')")
      .Example("files . --gitignore --no-hidden --ext cs", "fd-style: source files only")
      .Example("files . --text", "Find all text files (skip binaries)")
      .ComposesWith("wargs",
          "files ... | wargs <command>",
          "Find files then execute a command for each one (find | xargs pattern)")
      .ComposesWith("peep",
          "peep -- files . --glob '*.log' --newer 5m",
          "Watch for recently created log files on an interval")
      .ComposesWith("squeeze",
          "files . --glob '*.json' | wargs squeeze --zstd",
          "Compress all JSON files with zstd")
      .JsonField("path", "string", "File path (relative or absolute)")
      .JsonField("name", "string", "Filename only")
      .JsonField("type", "string", "file, directory, or symlink")
      .JsonField("size_bytes", "int", "File size in bytes (-1 for directories)")
      .JsonField("modified", "string", "ISO 8601 last modified timestamp")
      .JsonField("depth", "int", "Depth relative to search root")
      .JsonField("is_text", "bool?", "True if text file, false if binary. Present only when --text/--binary used.");
```

---

## Testing Strategy

### Winix.FileWalk.Tests (unit)

- **Predicate matching:** glob patterns (single, multiple OR), regex patterns, type filter, size range, date range, depth limit
- **Glob + regex AND interaction:** must match at least one from each group
- **Size parsing:** `100`, `100k`, `10M`, `1G`, edge cases, invalid input
- **Duration parsing:** `30s`, `5m`, `1h`, `7d`, `2w`, edge cases, invalid input
- **FileEntry construction:** all fields populated correctly
- **Hidden file detection:** dot-prefix on Unix, hidden attribute on Windows (platform-conditional tests)
- **Text/binary detection:** null-byte heuristic on known text files, known binary files, empty files, files shorter than 8KB

### Winix.Files.Tests (formatting)

- **Default format:** one path per line, correct line endings
- **Long format:** tab-delimited, size with commas, date format, type column
- **Null-delimited format:** `\0` separators, no trailing newline
- **NDJSON format:** valid JSON per line, all fields present, correct types
- **JSON summary:** standard fields (tool, version, exit_code, exit_reason), count, searched_roots
- **Colour output:** ANSI sequences present when enabled, absent when disabled

### Integration tests (temp directory trees)

- Walk a real directory tree with known structure, verify results match expected
- Gitignore filtering (requires git on PATH, create temp git repo with `.gitignore`)
- Symlink handling (platform-conditional — create symlinks, verify follow/no-follow)
- Multiple search roots
- Empty results (zero matches is success, exit 0)
- Permission denied on subdirectory (continue walking, warn on stderr)
- Non-existent root paths (error on all invalid, warn on partial)

---

## Implementation Plan (high-level)

### Phase 1: ShellKit enhancements

1. `GitIgnoreFilter` in ShellKit (wraps `git check-ignore --stdin`)
2. `--describe` fluent API and JSON serialiser on `CommandLineParser`
3. `--describe` added to `StandardFlags()`
4. Tests for both

### Phase 2: Winix.FileWalk library

5. `FileEntry`, `FileEntryType`, `FileWalkerOptions` types
6. `SizeParser` and `DurationParser` with tests
7. `FileWalker` — directory enumeration with predicate filtering
8. Glob matching (use `Microsoft.Extensions.FileSystemGlobbing` or `Matcher`)
9. Regex matching over filenames
10. Hidden file detection (cross-platform)
11. Symlink following with cycle detection
12. Integration with `GitIgnoreFilter`
13. Tests for all of the above

### Phase 3: Winix.Files library + console app

14. Formatting: default, long, print0, NDJSON, JSON summary
15. Console app: arg parsing, pipeline wiring, `--describe` metadata
16. Colour output for terminal mode
17. Tests for formatting

### Phase 4: AI discoverability docs

18. `docs/ai/files.md` — AI agent guide
19. `llms.txt` at repo root
20. README for `files` console app

### Phase 5: Retrofit

21. Add `--describe` metadata to timeit, squeeze, peep, wargs
22. Write AI guides for existing tools
23. Update `llms.txt` with all tools

---

## Future Work (v2)

**Parallel directory walking:** `fd` achieves 2-5x speedup on SSD/NVMe by walking directories in parallel. The `FileWalker` interface (`IEnumerable<FileEntry>`) doesn't prevent this — the walker can enumerate subdirectories concurrently internally and yield results as they arrive. v1 uses single-threaded `Directory.EnumerateFileSystemEntries`; v2 can add a `--threads` option or auto-detect based on drive type. The `IEnumerable` return type may need to become `IAsyncEnumerable` or use a channel internally for concurrent producers.
