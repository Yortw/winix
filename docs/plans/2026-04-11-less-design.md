# less — Native Pager for Windows

**Date:** 2026-04-11
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`less` is a native, cross-platform terminal pager for the Winix suite. It displays text one screen at a time with scrolling, searching, and ANSI colour passthrough. Built primarily to fill the gap on Windows (which has no native pager), it also integrates with the Winix `man` viewer as its preferred pager.

**Why it's needed:** Windows has no usable pager. `more.com` is forward-only, destroys ANSI escapes, and has no search. Git Bash ships `less` but it only works inside the MSYS2 environment. A native Windows pager means `git diff | less`, `files --glob "*.cs" | less`, and `man timeit` all work properly in CMD, PowerShell, and Windows Terminal.

**Cross-platform:** Builds for all 4 RIDs (win-x64, linux-x64, osx-x64, osx-arm64) like all Winix tools. On Linux/macOS it works but isn't solving a pain point — GNU less is already there.

---

## Project Structure

```
src/Winix.Less/            — class library (input, screen, search, follow mode)
src/less/                  — thin console app (option parsing, orchestration)
tests/Winix.Less.Tests/    — xUnit tests
```

Follows standard Winix conventions: library does all work, console app is thin, ShellKit provides arg parsing and terminal detection.

---

## Data Flow

```
input (stdin pipe or file)
    → InputSource → line buffer (string[])
    → Screen → visible window of lines → terminal output
    → SearchEngine → highlight/jump to matches
    → FollowMode → watch for new content, auto-scroll
```

---

## Components

### InputSource

Reads content from piped stdin or one or more file paths.

```csharp
public sealed class InputSource
{
    /// <summary>
    /// Creates an input source from stdin.
    /// Reads all available content into a line buffer.
    /// </summary>
    public static InputSource FromStdin();

    /// <summary>
    /// Creates an input source from a file path.
    /// </summary>
    public static InputSource FromFile(string filePath);

    /// <summary>File path, or "(stdin)" for piped input.</summary>
    public string Name { get; }

    /// <summary>All lines currently loaded.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>
    /// Checks for new content (for follow mode).
    /// Returns true if new lines were appended.
    /// </summary>
    public bool PollForNewContent();
}
```

For v1, content is loaded fully into memory. This is acceptable — even a 100MB log file is ~1M lines, well within memory. Streaming can be added later if needed.

For stdin, content is read until EOF. Follow mode on stdin watches for additional input after initial EOF (e.g., `tail -f log | less`).

For files, follow mode re-reads from the last known position using `FileStream.Seek`.

### Screen

Manages the terminal display: renders visible lines, handles ANSI passthrough, draws the status bar, and manages raw mode input.

```csharp
public sealed class Screen : IDisposable
{
    /// <summary>
    /// Creates a screen with the given options.
    /// Enters raw mode (hides cursor, disables line buffering).
    /// </summary>
    public Screen(ScreenOptions options);

    /// <summary>
    /// Renders the current view: visible lines from the buffer,
    /// with optional line numbers, search highlighting, and status bar.
    /// </summary>
    public void Render(
        IReadOnlyList<string> lines,
        int topLine,
        int leftColumn,
        ViewState state);

    /// <summary>
    /// Reads a single key from the terminal.
    /// </summary>
    public ConsoleKeyInfo ReadKey();

    /// <summary>
    /// Reads a line of text from the status bar area (for search input).
    /// </summary>
    public string? ReadPrompt(char promptChar);

    /// <summary>
    /// Restores terminal state.
    /// </summary>
    public void Dispose();
}
```

**ANSI handling:** Lines are rendered with ANSI escapes intact (colour passthrough). Width calculations use visible length (strip ANSI sequences) — same pattern as `man`'s `VisibleLength`/`StripAnsi`.

**Line wrapping vs truncation:** Controlled by `ChopLongLines` option:
- `false` (default): soft-wrap long lines onto subsequent rows. The line buffer position accounts for wrapped display lines.
- `true` (`-S`): truncate at terminal width. Left/right arrow keys shift `leftColumn` to pan horizontally.

**Line numbers:** When enabled (`-N`), a fixed-width gutter (6 chars) shows line numbers before each line. Wrapped continuation lines show blank gutters.

### SearchEngine

Forward and backward search through the line buffer.

```csharp
public sealed class SearchEngine
{
    /// <summary>
    /// Searches forward from startLine for a line matching the pattern.
    /// Wraps around to the beginning if not found.
    /// </summary>
    public int? FindNext(IReadOnlyList<string> lines, string pattern, int startLine);

    /// <summary>
    /// Searches backward from startLine.
    /// Wraps around to the end if not found.
    /// </summary>
    public int? FindPrevious(IReadOnlyList<string> lines, string pattern, int startLine);

    /// <summary>The most recent search pattern, or null.</summary>
    public string? CurrentPattern { get; }

    /// <summary>Whether search is case-insensitive.</summary>
    public bool IgnoreCase { get; set; }

    /// <summary>Whether to force case-insensitive even when pattern has uppercase.</summary>
    public bool SmartCase { get; set; }
}
```

Search operates on ANSI-stripped text (visible characters only). The original ANSI-formatted line is displayed — search matching doesn't break colour.

Case sensitivity:
- Default: case-sensitive
- `-i`: case-insensitive unless pattern contains uppercase (smart case)
- `-I`: always case-insensitive

### FollowMode

Watches the input source for new content and auto-scrolls to the bottom.

```csharp
public sealed class FollowMode
{
    /// <summary>
    /// Enters follow mode. Polls the input source for new content
    /// and calls the render callback when new lines appear.
    /// Exits when the user presses a key (typically Ctrl-C or any key).
    /// </summary>
    public void Enter(
        InputSource source,
        Action onNewContent,
        Func<bool> checkForKeyPress);
}
```

Follow mode polls at ~250ms intervals. Displays a "Waiting for data... (press any key to stop)" indicator on the status bar. Any keypress exits follow mode and returns to normal navigation.

Activated by:
- `+F` startup option
- `F` key during viewing
- `Shift+F` (alias)

### LessOptions

Parsed from CLI flags and `LESS` env var.

```csharp
public sealed class LessOptions
{
    public bool ShowLineNumbers { get; init; }     // -N
    public bool ChopLongLines { get; init; }       // -S
    public bool QuitIfOneScreen { get; init; }     // -F (default: true)
    public bool RawAnsi { get; init; }             // -R (default: true)
    public bool NoClearOnExit { get; init; }       // -X (default: true)
    public bool IgnoreCase { get; init; }          // -i
    public bool ForceIgnoreCase { get; init; }     // -I
    public bool FollowOnStart { get; init; }       // +F
    public string? InitialSearch { get; init; }    // +/pattern
    public bool StartAtEnd { get; init; }          // +G

    /// <summary>
    /// Resolves options from defaults, LESS env var, and CLI flags.
    /// Precedence: CLI flags > LESS env var > built-in defaults.
    /// When LESS env var is set (non-empty), it replaces defaults entirely.
    /// When LESS env var is unset/empty, built-in defaults apply.
    /// </summary>
    public static LessOptions Resolve(string[] cliFlags, string? lessEnvVar);
}
```

**Flag precedence:**

```
LESS env unset or ""  →  defaults: F, R, X on; all others off
LESS env non-empty    →  only the listed flags are on (replaces defaults entirely)
CLI flags             →  override on top of the resolved set
```

Unset and empty string (`LESS=""` or `LESS=`) are treated identically — both mean "use built-in defaults".

**Built-in defaults** (modern, matches `git`'s `LESS=FRX`):
- `-F` (quit if one screen) — on
- `-R` (raw ANSI) — on
- `-X` (no clear on exit) — on
- Everything else — off

**Supported `LESS` env var characters:** `N`, `S`, `F`, `R`, `X`, `i`, `I`. Unknown characters are silently ignored.

---

## Key Bindings

| Key | Action |
|-----|--------|
| `q`, `Q` | Quit |
| `j`, `↓`, `Enter` | Scroll down one line |
| `k`, `↑` | Scroll up one line |
| `Space`, `PgDn`, `d` | Page down |
| `PgUp`, `u` | Page up |
| `g`, `Home` | Go to top |
| `G`, `End` | Go to bottom |
| `→` | Scroll right (when `-S` chop mode) |
| `←` | Scroll left (when `-S` chop mode) |
| `/` | Forward search |
| `?` | Backward search |
| `n` | Next search match |
| `N` | Previous search match |
| `F` | Enter follow mode |
| `-N` | Toggle line numbers (press `-` then `N`) |
| `-S` | Toggle chop long lines (press `-` then `S`) |

---

## Status Bar

Displayed at the bottom of the terminal in reverse video:

```
 file.txt  Lines 42-84/1200 (7%)
```

Variants:
- Piped input: `(stdin)  Lines 42-84/1200 (7%)`
- During search: `(stdin)  /pattern  Lines 42-84/1200 (7%)`
- Follow mode: `Waiting for data... (press any key to stop)`
- End of file: `(END)`

---

## Quit-If-One-Screen (-F)

When enabled (default), if the entire content fits within one terminal screen:
1. Display the content directly to stdout (no pager UI)
2. Exit immediately with code 0

This means short output from `git diff` or `man` just prints and returns — no need to press `q`. Only longer content enters the interactive pager.

Combined with `-X` (no clear on exit), the content remains visible in the terminal scrollback after quitting.

---

## Integration with man

Update `PagerChain` in `Winix.Man` to prefer a sibling `less` binary. The current priority is:

```
$MANPAGER → $PAGER → sibling less → system less → built-in pager → stdout
```

The sibling check (`{exe_dir}/less` or `{exe_dir}/less.exe`) already exists in `PagerChain`. When `less` is installed alongside `man` (scoop, combined zip), it will be picked up automatically with no configuration needed.

`man` currently sets no env vars when invoking the pager. The pager receives ANSI-formatted content on stdin and displays it. This works with both GNU `less` and our `less`.

---

## Testing Strategy

**Unit-testable (class library):**
- `InputSource` — read from file, read from string (simulated stdin), poll for new content
- `SearchEngine` — forward/backward search, case sensitivity, wrap-around, ANSI stripping
- `LessOptions.Resolve` — default flags, env var override, CLI flag override, precedence
- Line wrapping logic — split long lines into display rows, visible width calculations

**Hard to unit test (interactive terminal):**
- `Screen` — requires real terminal, tested manually
- `FollowMode` — requires async I/O, tested manually
- Key handling loop — tested manually

**Integration tests:**
- Full pipeline: file → InputSource → verify line count and content
- Options resolution: various combinations of env var + CLI flags

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (file not found, read error) |
| 2 | Usage error (bad arguments) |

---

## Scope Boundaries

**In scope (v1):**
- Piped stdin and single file input
- ANSI colour passthrough
- Scrolling (vertical + horizontal with `-S`)
- Forward and backward search with case options
- Follow mode (`F` / `+F`)
- Line numbers (`-N`)
- `LESS` env var support
- GNU-compatible common flags: `-N`, `-S`, `-R`, `-F`, `-X`, `-i`, `-I`, `+F`, `+/pattern`, `+G`
- Runtime toggle of `-N` and `-S` via dash-key sequences
- Quit-if-one-screen default
- Status bar

**Deferred (v1.1):**
- Mouse wheel scrolling — requires Win32 `ReadConsoleInput` with `ENABLE_MOUSE_INPUT`; .NET `Console.ReadKey` doesn't capture mouse events. Worth adding but needs platform-specific code.

**Out of scope (v1):**
- Multiple file support (`:n`/`:p`)
- `LESSOPEN` filter pipe
- Editing (`v` to open editor)
- Marks (`m`/`'`)
- Tags file support
- Custom key bindings
- Custom prompt format (`-P`)
- Tab stop configuration (`-x`)
