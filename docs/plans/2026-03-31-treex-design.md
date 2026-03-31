# treex — Enhanced Directory Tree Viewer

**Date:** 2026-03-31
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`treex` is a cross-platform directory tree viewer with colour, filtering, size rollups, `.gitignore` awareness, and OSC 8 clickable hyperlinks. For humans, not pipes — the visual complement to `files` (flat stream for machines).

**Why not just `tree`?** Windows native `tree` is DOS-era — no colour, no filtering, no sizes, no gitignore. Linux `tree` is better but lacks clickable links, JSON output, and `--describe` for AI agents. `treex` fills the same gap the other Winix tools fill: modern, cross-platform, machine-friendly when needed.

---

## Project Structure

```
src/Winix.TreeX/             — class library (tree building, rendering, size rollup)
src/treex/                   — thin console app (arg parsing, call library, exit code)
tests/Winix.TreeX.Tests/     — xUnit tests
```

Shares `Winix.FileWalk` for predicate helpers (GlobMatcher, ContentDetector, SizeParser, DurationParser) and `Yort.ShellKit` for GitIgnoreFilter, CommandLineParser, AnsiColor, ConsoleEnv.

---

## Data Flow

```
root paths (args or cwd)
  → TreeBuilder (Winix.TreeX)
    → recursive directory walk
    → applies predicates (glob, regex, type, size, date, depth, hidden, gitignore)
    → prunes empty branches when filtering (keep ancestors of matches)
    → computes size rollups when --size
    → builds TreeNode hierarchy
  → TreeRenderer (Winix.TreeX)
    → walks TreeNode tree
    → emits tree-line prefixes (├──, └──, │)
    → applies colour (dirs blue, symlinks cyan, executables green, connectors dim)
    → applies OSC 8 hyperlinks (file:/// URLs)
    → optional size/date columns (right-aligned)
  → stdout
  → Summary line to stderr
```

---

## Architecture: Why Not Reuse FileWalker Directly

`FileWalker` yields a flat `IEnumerable<FileEntry>` stream — perfect for `files` but wrong for tree rendering. Tree rendering needs:

1. **Parent-child relationships** — to draw `├──` vs `└──` (is this the last sibling?)
2. **Size rollups** — directory size = sum of descendants
3. **Branch pruning** — when filtering, keep ancestor directories of matches but prune empty branches
4. **Sorted output** — directories first, then files, both alphabetical

These require building a tree in memory. A flat stream can't express sibling position or aggregate sizes.

**What IS shared:** The predicate helpers from `Winix.FileWalk`:
- `GlobMatcher` — glob pattern matching with case sensitivity
- `ContentDetector` — text/binary detection (if `--text`/`--binary` added later)
- `SizeParser` — parse `100k`, `10M`, `1G`
- `DurationParser` — parse `1h`, `7d`, `2w`

And from `Yort.ShellKit`:
- `GitIgnoreFilter` — gitignore checking
- `CommandLineParser` — arg parsing with `--describe`
- `AnsiColor` — colour helpers
- `ConsoleEnv` — terminal detection, VT processing

---

## Components

### TreeNode

In-memory tree structure built during directory walking.

```csharp
public sealed class TreeNode
{
    public string Name { get; }
    public string FullPath { get; }
    public FileEntryType Type { get; }
    public long SizeBytes { get; set; }       // -1 initially for dirs, computed during rollup
    public DateTimeOffset Modified { get; }
    public bool IsExecutable { get; }
    public bool IsMatch { get; }              // true if this entry matches filters (vs ancestor kept for structure)
    public List<TreeNode> Children { get; }
}
```

`IsMatch` distinguishes entries that matched filters from ancestor directories kept only to show the path. When not filtering, all entries have `IsMatch = true`.

### TreeBuilder

Builds the `TreeNode` hierarchy from the filesystem.

```csharp
public sealed class TreeBuilder
{
    public TreeBuilder(TreeBuilderOptions options, Func<string, bool>? isIgnored = null);

    /// <summary>
    /// Builds a tree rooted at the given path. Returns the root TreeNode.
    /// </summary>
    public TreeNode Build(string rootPath);
}
```

Responsibilities:
- Recursive directory enumeration
- Apply predicates (glob, regex, ext, type, size, date, hidden, gitignore)
- Sort: directories first (alphabetical), then files (alphabetical)
- Prune empty branches (directories with no matching descendants)
- Compute size rollups when requested

### TreeBuilderOptions

```csharp
public sealed record TreeBuilderOptions(
    IReadOnlyList<string> GlobPatterns,
    IReadOnlyList<string> RegexPatterns,
    FileEntryType? TypeFilter,
    long? MinSize,
    long? MaxSize,
    DateTimeOffset? NewerThan,
    DateTimeOffset? OlderThan,
    int? MaxDepth,
    bool IncludeHidden,
    bool UseGitIgnore,
    bool CaseInsensitive,
    bool ComputeSizes,
    SortMode Sort);
```

### SortMode

```csharp
public enum SortMode
{
    Name,       // alphabetical, directories first (default)
    Size,       // largest first, directories first
    Modified    // newest first, directories first
}
```

### TreeRenderer

Walks a `TreeNode` tree and produces formatted output lines.

```csharp
public sealed class TreeRenderer
{
    public TreeRenderer(TreeRenderOptions options);

    /// <summary>
    /// Renders a tree to the given writer. Returns the count of directories and files.
    /// </summary>
    public TreeStats Render(TreeNode root, TextWriter writer);
}
```

### TreeRenderOptions

```csharp
public sealed record TreeRenderOptions(
    bool UseColor,
    bool UseLinks,          // OSC 8 hyperlinks
    bool ShowSize,          // right-aligned size column
    bool ShowDate,          // date column
    bool DirsOnly);         // --dirs-only: suppress files, show only directory structure
```

### TreeStats

```csharp
public sealed record TreeStats(
    int DirectoryCount,
    int FileCount,
    long TotalSizeBytes);   // -1 if sizes not computed
```

---

## CLI Flags

| Flag | Short | Value | Description |
|------|-------|-------|-------------|
| `--glob` | `-g` | PATTERN | Match filenames against glob (repeatable, OR) |
| `--regex` | `-e` | PATTERN | Match filenames against regex (repeatable, OR) |
| `--ext` | | EXT | Match file extension (repeatable, OR) |
| `--type` | `-t` | TYPE | Filter by type: `f` (file), `d` (directory), `l` (symlink) |
| `--min-size` | | SIZE | Minimum file size |
| `--max-size` | | SIZE | Maximum file size |
| `--newer` | | DURATION | Modified within duration |
| `--older` | | DURATION | Modified before duration |
| `--max-depth` | `-d` | N | Maximum directory depth |
| `--no-hidden` | | | Skip hidden/dot files and directories |
| `--gitignore` | | | Respect `.gitignore` |
| `--ignore-case` | `-i` | | Case-insensitive pattern matching |
| `--case-sensitive` | | | Case-sensitive pattern matching |
| `--size` | `-s` | | Show file sizes (right-aligned column, human-readable units) |
| `--date` | | | Show last modified date |
| `--sort` | | MODE | Sort: `name` (default), `size`, `modified` |
| `--dirs-only` | `-D` | | Show only directories (suppress files) |
| `--no-links` | | | Disable OSC 8 hyperlinks |
| `--json` | | | JSON summary to stderr |
| `--ndjson` | | | Streaming NDJSON per entry to stdout |
| `--help` | `-h` | | Show help |
| `--version` | | | Show version |
| `--describe` | | | Structured metadata for AI agents |
| `--color` | | | Force colour on |
| `--no-color` | | | Force colour off |

**Positional args:** Root paths to display. Defaults to `.` (cwd). Multiple roots supported — rendered as separate trees with a blank line between.

### Default Behaviour Summary

| Behaviour | Default | Override |
|-----------|---------|----------|
| Hidden files | Included | `--no-hidden` to skip |
| .gitignore | Not respected | `--gitignore` to enable |
| Sizes | Hidden | `--size` to show |
| Dates | Hidden | `--date` to show |
| Sort | Alphabetical, dirs first | `--sort size\|modified` |
| Colour | Auto-detect terminal | `--color` / `--no-color` |
| Hyperlinks | Auto-detect terminal | `--no-links` to disable |
| Case sensitivity | Platform-aware | `--ignore-case` / `--case-sensitive` |
| Filtering | Show full tree | Glob/regex/ext prune to matching branches |

---

## Output Formats

### Default (tree)

```
src/
├── files/
│   ├── files.csproj
│   ├── Program.cs
│   └── README.md
├── Winix.FileWalk/
│   ├── ContentDetector.cs
│   ├── FileEntry.cs
│   ├── FileWalker.cs
│   └── SizeParser.cs
└── Yort.ShellKit/
    ├── AnsiColor.cs
    ├── CommandLineParser.cs
    └── ConsoleEnv.cs

3 directories, 10 files
```

Colour when terminal detected:
- Directories: blue
- Symlinks: cyan
- Executables: green
- Tree connectors (`├──`, `└──`, `│`): dim
- Sizes (when `--size`): dim
- Dates (when `--date`): dim

OSC 8 hyperlinks on names when terminal supports it.

### With `--size`

```
src/                         48.2K
├── files/                   18.1K
│   ├── files.csproj           842
│   ├── Program.cs           13.1K
│   └── README.md             4.8K
├── Winix.FileWalk/          16.4K
│   ├── ContentDetector.cs    1.2K
│   ├── FileEntry.cs            964
│   ├── FileWalker.cs         9.8K
│   └── SizeParser.cs         2.9K
└── Yort.ShellKit/           13.7K
    ├── AnsiColor.cs            620
    ├── CommandLineParser.cs  11.2K
    └── ConsoleEnv.cs          2.4K

3 directories, 10 files (48.2K)
```

Sizes right-aligned in a column. Human-readable units: bytes below 1K, then K, M, G. One decimal place for K/M/G.

### With `--size --date`

```
src/                         48.2K  2026-03-31 20:31
├── files/                   18.1K  2026-03-31 20:31
│   ├── files.csproj           842  2026-03-31 14:22
│   ├── Program.cs           13.1K  2026-03-31 20:31
│   └── README.md             4.8K  2026-03-31 19:00
...
```

### Filtered (pruned branches)

`treex src --ext cs`:

```
src/
├── files/
│   └── Program.cs
├── Winix.FileWalk/
│   ├── ContentDetector.cs
│   ├── FileEntry.cs
│   ├── FileWalker.cs
│   └── SizeParser.cs
└── Yort.ShellKit/
    ├── AnsiColor.cs
    ├── CommandLineParser.cs
    └── ConsoleEnv.cs

3 directories, 10 files
```

Ancestor directories kept to show path structure. Empty branches pruned.

### NDJSON (`--ndjson`)

One JSON object per entry to stdout:

```json
{"tool":"treex","version":"0.2.0","exit_code":0,"exit_reason":"success","path":"src/files/Program.cs","name":"Program.cs","type":"file","size_bytes":13112,"modified":"2026-03-31T07:31:45+00:00","depth":2}
```

Standard Winix envelope fields on every line.

### JSON summary (`--json`)

Summary to stderr after rendering:

```json
{"tool":"treex","version":"0.2.0","exit_code":0,"exit_reason":"success","directories":3,"files":10,"total_size_bytes":49356}
```

`total_size_bytes` present only when `--size` is active.

---

## Colour and Hyperlinks

### Colour by Entry Type

| Type | Colour | ANSI Code |
|------|--------|-----------|
| Directory | Blue | `\x1b[34m` |
| Symlink | Cyan | `\x1b[36m` |
| Executable | Green | `\x1b[32m` |
| Regular file | Default | (none) |
| Tree connectors | Dim | `\x1b[2m` |
| Size/date columns | Dim | `\x1b[2m` |

### Executable Detection

- **Unix:** check `S_IXUSR` permission bit via `File.GetUnixFileMode()` (.NET 7+)
- **Windows:** check extension against known executable set (`.exe`, `.cmd`, `.bat`, `.ps1`, `.com`)

### OSC 8 Hyperlinks

Clickable links on file/directory names. Click opens the file in the default editor or directory in the file manager.

Format: `\x1b]8;;file:///absolute/path\x1b\\display text\x1b]8;;\x1b\\`

- **On by default** when colour is enabled (colour implies terminal capable of ANSI = likely supports OSC 8)
- **`--no-links`** to disable
- **Piped output:** links suppressed automatically (colour off = links off)
- Paths must be absolute in the URL, even when display shows relative names

---

## Size Formatting

Human-readable sizes with one decimal place:

| Bytes | Display |
|-------|---------|
| 0–999 | `0` – `999` (plain bytes) |
| 1,000–1,023 | `1,000` – `1,023` (still bytes, under 1K threshold) |
| 1,024+ | `1.0K` – `999.9K` |
| 1,048,576+ | `1.0M` – `999.9M` |
| 1,073,741,824+ | `1.0G`+ |

Right-aligned in a column. Column width determined by the widest size string in the tree.

### Size Rollups

When `--size` is active, directory sizes are the sum of all descendant file sizes. Computed bottom-up after the tree is built. If filtering is active, directory sizes reflect only the visible (matching) files, not the full directory.

---

## Filtering and Pruning

When glob/regex/ext/type/size/date filters are active:

1. Walk the entire tree, marking entries that match filters (`IsMatch = true`)
2. Directories are never directly matched by glob/regex/ext (same as `files`)
3. After marking, prune: remove directories that have no matching descendants
4. Remaining directories have `IsMatch = false` but are kept as structural ancestors

This produces a pruned tree showing only the paths to matching files, with clean tree structure.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success (found zero or more entries) |
| 1 | Runtime error (permission denied on root, invalid path) |
| 125 | Usage error |

---

## Error Handling

- **Permission denied on subdirectory:** Skip with stderr warning, continue rendering
- **Permission denied on root:** Exit 1
- **Non-existent root:** Error on stderr. If multiple roots and some valid, render valid ones. If all invalid, exit 1.
- **Symlink cycles:** Detected and skipped when `--follow` would be added (not in v1 — symlink directories shown but not followed)

---

## `--describe` Metadata

```csharp
parser.Platform("cross-platform",
          replaces: new[] { "tree" },
          valueOnWindows: "Windows tree is DOS-era — no colour, filtering, or sizes",
          valueOnUnix: "Adds clickable hyperlinks, size rollups, gitignore, JSON output")
      .StdinDescription("Not used")
      .StdoutDescription("Tree-formatted directory listing. NDJSON with --ndjson.")
      .StderrDescription("Summary line, errors, and --json output.")
      .Example("treex", "Show current directory tree")
      .Example("treex src --ext cs", "Show only C# files with ancestor directories")
      .Example("treex --size --gitignore --no-hidden", "Clean tree with sizes")
      .Example("treex --size --sort size", "Find largest files")
      .Example("treex -d 2", "Limit to 2 levels deep")
      .Example("treex src tests", "Show multiple roots")
      .ComposesWith("files", "files . --ext cs | head vs treex . --ext cs", "files for piping, treex for visual")
      .JsonField("path", "string", "File path relative to root")
      .JsonField("name", "string", "Filename only")
      .JsonField("type", "string", "file, dir, or link")
      .JsonField("size_bytes", "int", "File size (-1 for directories without --size)")
      .JsonField("modified", "string", "ISO 8601 last modified")
      .JsonField("depth", "int", "Depth relative to root");
```

---

## Testing Strategy

### Winix.TreeX.Tests

**TreeBuilder tests:**
- Build from temp directory tree, verify node structure
- Filtering prunes empty branches
- Size rollups sum correctly
- Sort: dirs first + alphabetical, size sort, modified sort
- Hidden file exclusion
- Depth limiting
- Multiple filters (glob + size)

**TreeRenderer tests:**
- Render known tree structure, verify tree-line characters (├──, └──, │)
- Last child gets └──, others get ├──
- Nested indentation uses │ for non-last parents, spaces for last parents
- Colour codes present when enabled, absent when disabled
- Size column right-aligned when enabled
- Date column when enabled
- OSC 8 link format correct (file:/// URL wrapping name)
- Summary line format ("N directories, M files")
- Summary includes total size when --size active

**Formatting tests:**
- Human-readable size formatting (bytes, K, M, G)
- NDJSON line format
- JSON summary format

**Integration tests:**
- Temp directory tree, build + render, verify complete output
- Gitignore filtering (temp git repo)
- Multiple roots render with blank line between

---

## Implementation Plan (high-level)

### Phase 1: Project scaffolding
1. Create Winix.TreeX, treex, Winix.TreeX.Tests projects
2. Add to solution

### Phase 2: Core types and tree building
3. TreeNode, TreeBuilderOptions, SortMode, TreeStats
4. Human-readable size formatter
5. TreeBuilder — recursive walk, predicate filtering, sorting
6. Branch pruning for filtered trees
7. Size rollups

### Phase 3: Rendering
8. TreeRenderer — tree lines, indentation tracking
9. Colour support (dirs blue, symlinks cyan, executables green, connectors dim)
10. OSC 8 hyperlink rendering
11. Size and date columns (right-aligned)
12. Summary line

### Phase 4: Console app
13. Program.cs — arg parsing, pipeline wiring, --describe metadata
14. NDJSON and JSON output formats

### Phase 5: Docs and integration
15. README, AI guide, llms.txt update, CLAUDE.md update
16. Release pipeline, scoop manifest
