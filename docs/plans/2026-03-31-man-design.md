# man — Cross-Platform Man Page Viewer

**Date:** 2026-03-31
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`man` is a native, cross-platform man page viewer for the Winix suite. It parses and renders groff man pages directly to the terminal with ANSI colour and OSC 8 hyperlinks, eliminating the need for groff/troff on Windows while working identically on Linux and macOS.

**Why it's needed:** Windows has no native `man` command. Git Bash ships one (via MSYS2 groff) but it's slow, doesn't support colour well, and can't be used outside the MSYS2 environment. A native viewer that handles standard man page formats means `man timeit` works out of the box on every platform, and on Windows it can also read man pages from Git, MSYS2, and any other source.

**Authoring model:** Winix man pages are authored in Markdown and converted to groff `.1` files at build time (via `pandoc`). Only the generated groff is shipped. This means:
- Authoring is pleasant (Markdown)
- Distribution is standard (groff) — works with system `man` on Linux/macOS
- Rendering is consistent (winix `man` reads groff on every platform)

---

## Project Structure

```
src/Winix.Man/             — class library (parser, renderer, discovery, pager)
src/man/                   — thin console app (arg parsing, call library, exit code)
tests/Winix.Man.Tests/     — xUnit tests
```

Follows standard Winix conventions: library does all work, console app is thin, ShellKit provides arg parsing and terminal detection.

---

## Data Flow

```
page name + section
    → PageDiscovery → file path (or "not found")
    → FileReader → raw groff text (transparent .gz decompression)
    → Lexer → token stream (requests, text lines, escapes)
    → MacroExpander → intermediate representation (styled blocks)
    → TerminalRenderer → ANSI-formatted text
    → Pager → interactive display (or stdout if piped)
```

---

## Components

### PageDiscovery

Locates man page files given a name and optional section number.

**Search order** (first match wins):

1. **Bundled pages** — shipped alongside the `man` binary at `{exe_dir}/man/man{section}/{name}.{section}`
2. **`MANPATH` entries** — colon-separated on Unix, semicolon-separated on Windows. Each entry follows standard hierarchy: `{path}/man{section}/{name}.{section}`
3. **Auto-detected well-known locations:**
   - **Windows:** Git for Windows (`{git_install}/usr/share/man`), MSYS2 (`{msys2}/usr/share/man`)
   - **macOS:** `/usr/share/man`, Homebrew (`/opt/homebrew/share/man`, `/usr/local/share/man`), Xcode command-line tools (`/Library/Developer/CommandLineTools/usr/share/man`)
   - **Linux:** `/usr/share/man`, `/usr/local/share/man`

**Section handling:**
- `man timeit` — searches all sections, prefers section 1
- `man 3 printf` — searches only section 3
- Multiple matches: prefer lower section number (standard behaviour)

**File extensions:** Checks for both `{name}.{section}` and `{name}.{section}.gz`. Compressed pages are transparently decompressed via `GZipStream`.

```csharp
public sealed class PageDiscovery
{
    public PageDiscovery(IReadOnlyList<string> searchPaths);

    /// <summary>
    /// Resolves the file path for a man page, searching bundled pages,
    /// MANPATH, and auto-detected locations in order.
    /// Returns null if no matching page is found.
    /// </summary>
    public string? FindPage(string name, int? section = null);

    /// <summary>
    /// Returns the effective search path (bundled + MANPATH + auto-detected),
    /// for use with --manpath.
    /// </summary>
    public IReadOnlyList<string> GetEffectiveSearchPath();
}
```

**Auto-detection heuristics:**
- Git for Windows: check registry (`HKLM\SOFTWARE\GitForWindows\InstallPath`) and common paths (`C:\Program Files\Git`)
- MSYS2: check `MSYS2_ROOT` env var and common paths (`C:\msys64`)
- Homebrew on macOS: check `HOMEBREW_PREFIX` env var, then `/opt/homebrew` (ARM) and `/usr/local` (Intel)

### FileReader

Reads a man page file, transparently decompressing `.gz` files.

```csharp
public static class ManPageFileReader
{
    /// <summary>
    /// Reads the content of a man page file. Transparently decompresses
    /// gzip-compressed files (detected by .gz extension).
    /// </summary>
    public static string Read(string filePath);
}
```

### Lexer

Tokenises raw groff source into a stream of tokens. Does not interpret macros — just identifies structure.

**Token types:**
- `Request` — a line starting with `.` (e.g. `.SH NAME`). Contains the macro name and arguments.
- `TextLine` — a line of body text, may contain inline escapes
- `Comment` — a `.\"` line (discarded by the expander)

**Inline escapes** recognised during lexing:
- `\fB`, `\fI`, `\fR`, `\fP` — font changes (bold, italic, roman, previous)
- `\-` — hyphen-minus
- `\\` — literal backslash
- `\(xx` — two-character special character (e.g. `\(em` for em-dash)
- `\e` — escape character (backslash)
- `\~`, `\&` — non-breaking space, zero-width space

```csharp
public sealed class GroffLexer
{
    /// <summary>
    /// Tokenises groff source text into a stream of tokens.
    /// </summary>
    public IEnumerable<GroffToken> Tokenise(string source);
}
```

### MacroExpander

Interprets macro tokens and produces an intermediate representation of styled document blocks. Two implementations — one for `man` macros, one for `mdoc` macros — producing the same IR.

**Intermediate representation types:**

```csharp
public abstract record DocumentBlock;

public record TitleBlock(string Name, string Section, string Date, string Source, string Manual) : DocumentBlock;

public record SectionHeading(string Text) : DocumentBlock;

public record SubsectionHeading(string Text) : DocumentBlock;

public record Paragraph(IReadOnlyList<StyledSpan> Content) : DocumentBlock;

public record TaggedParagraph(IReadOnlyList<StyledSpan> Tag, IReadOnlyList<StyledSpan> Body) : DocumentBlock;

public record IndentedParagraph(IReadOnlyList<StyledSpan> Content, int Indent) : DocumentBlock;

public record PreformattedBlock(string Text) : DocumentBlock;

public record VerticalSpace(int Lines) : DocumentBlock;

public record StyledSpan(string Text, FontStyle Style);

[Flags]
public enum FontStyle
{
    Roman = 0,
    Bold = 1,
    Italic = 2
}
```

**Auto-detection:** If the first request is `.TH`, use the `man` macro expander. If it's `.Dd` or `.Dt`, use the `mdoc` expander.

**MVP — `man` macro expander:**

| Macro | IR Output |
|-------|-----------|
| `.TH` | `TitleBlock` |
| `.SH` | `SectionHeading` |
| `.SS` | `SubsectionHeading` |
| `.PP` / `.P` / `.LP` | `Paragraph` (starts new paragraph) |
| `.TP` | `TaggedParagraph` (next line is tag, following lines are body) |
| `.IP` | `IndentedParagraph` |
| `.RS` / `.RE` | Increase / decrease indent level |
| `.B` / `.I` | `StyledSpan` with Bold / Italic |
| `.BI` / `.BR` / `.IB` / `.IR` / `.RB` / `.RI` | Alternating styled spans |
| `.nf` / `.fi` | Enter / exit `PreformattedBlock` |
| `.sp` | `VerticalSpace` |

```csharp
public interface IManMacroExpander
{
    /// <summary>
    /// Expands a stream of groff tokens into a sequence of document blocks.
    /// </summary>
    IReadOnlyList<DocumentBlock> Expand(IEnumerable<GroffToken> tokens);
}

public sealed class ManMacroExpander : IManMacroExpander { ... }
```

**Phase 2 — `mdoc` macro expander:**

Separate class, same `IManMacroExpander` interface. Handles `.Dd`, `.Dt`, `.Nm`, `.Nd`, `.Sh`, `.Ss`, `.Bl`/`.It`/`.El` (lists), `.Fl` (flag), `.Ar` (argument), `.Op` (optional), `.Xr` (cross-reference), `.Pa` (path), `.Ev` (environment variable), etc.

The semantic nature of `mdoc` macros maps naturally to the IR — `.Fl v` becomes a `StyledSpan` with Bold style and "-v" text, `.Xr grep 1` becomes a styled span that also carries cross-reference metadata for the renderer.

### TerminalRenderer

Takes the intermediate representation and produces ANSI-formatted text for terminal display. Uses ShellKit's `ConsoleEnv` for capability detection.

**Rendering rules:**

| Block Type | Rendering |
|------------|-----------|
| `TitleBlock` | Header line: `NAME(SECTION)` left-justified, centred, and right-justified |
| `SectionHeading` | Bold + colour, no indent, blank line before |
| `SubsectionHeading` | Bold, indented 3 spaces, blank line before |
| `Paragraph` | Wrapped to width, indented 7 spaces (standard man indent) |
| `TaggedParagraph` | Tag at indent 7, body at indent 15 (or next line if tag is long) |
| `IndentedParagraph` | Like paragraph but at current indent level |
| `PreformattedBlock` | No wrapping, indented 7 spaces, no font interpretation |
| `VerticalSpace` | Blank lines |

**Font styles:**
- `Bold` → ANSI bold (`\e[1m`)
- `Italic` → ANSI underline (`\e[4m`) — most terminals don't support true italic; underline is the standard man page convention
- `Roman` → ANSI reset (`\e[0m`)

**Colour** (when supported):
- Section headings — bold + configurable colour (default: cyan)
- Cross-references (e.g. `grep(1)`) — distinct colour (default: blue)
- Regular bold/italic — standard ANSI bold/underline (no extra colour)

**OSC 8 hyperlinks** (when supported):
- Cross-references — detected by pattern matching `word(N)` in rendered text (e.g. `grep(1)`, `printf(3)`). In `mdoc` mode, `.Xr` macros carry this metadata explicitly. Clickable via OSC 8 with URI `man:{name}({section})` as a convention. Fallback: no link, just styled text.
- Explicit URLs in man page text — passed through as clickable `https://` links
- File paths — emitted as `file:///` URIs where identifiable

**Width:** Terminal width via `ConsoleEnv.TerminalWidth`, capped at 80 columns for readability. Overridden by `--width=N` or `MANWIDTH` env var.

```csharp
public sealed class TerminalRenderer
{
    public TerminalRenderer(ConsoleEnv console, RendererOptions options);

    /// <summary>
    /// Renders document blocks to a string containing ANSI-formatted text,
    /// wrapped to the configured width.
    /// </summary>
    public string Render(IReadOnlyList<DocumentBlock> blocks);
}

public sealed class RendererOptions
{
    public int? WidthOverride { get; init; }
    public bool Color { get; init; }
    public bool Hyperlinks { get; init; }
}
```

### BuiltInPager

A minimal pager for interactive display. Used as last resort when no external pager is available.

**Capabilities:**
- Arrow keys / `j`/`k` — scroll line by line
- Page Up / Page Down / Space — scroll by page
- Home / End — jump to top / bottom
- `/` — forward search (highlight matches)
- `n` / `N` — next / previous search match
- `q` — quit

**Implementation:** Switches terminal to raw mode (disable line buffering and echo), reads keypresses, renders the visible window. Restores terminal state on exit. Uses ShellKit's console primitives where available.

**Not in scope:** Horizontal scroll, line editing, backward search, marks, multiple files, configuration. Just enough that reading a man page is comfortable.

```csharp
public sealed class BuiltInPager
{
    /// <summary>
    /// Displays text interactively with scrolling and search.
    /// Blocks until the user quits. Restores terminal state on exit.
    /// </summary>
    public void Display(string content);
}
```

### PagerChain

Determines which pager to use and invokes it.

**Priority:**
1. `$MANPAGER` environment variable
2. `$PAGER` environment variable
3. Sibling winix `less` (same directory as `man` binary)
4. System `less` on `PATH`
5. Built-in pager
6. Raw stdout (when stdout is piped / not a terminal)

```csharp
public sealed class PagerChain
{
    public PagerChain(ConsoleEnv console, string exeDirectory);

    /// <summary>
    /// Sends rendered content through the highest-priority available pager.
    /// If stdout is not a terminal, writes directly to stdout (no paging).
    /// </summary>
    public void Page(string content);
}
```

---

## CLI Interface

```
man [options] [[section] page]
man timeit              # show timeit(1)
man 3 printf            # show printf(3)
man -k compress         # search page descriptions (phase 2, requires index)
man --path timeit       # print path to man page file, don't render
man --where timeit      # alias for --path (GNU man compat)
man --manpath           # print effective search path
```

### Options

| Flag | Short | Purpose |
|------|-------|---------|
| `--no-pager` | | Write to stdout, no paging |
| `--color` | | Force colour output |
| `--no-color` | | Suppress colour output |
| `--width=N` | | Override rendering width (default: terminal width, max 80) |
| `--path` | `-w` | Print file path to man page instead of rendering |
| `--where` | | Alias for `--path` (GNU man compatibility) |
| `--manpath` | | Print the effective search path and exit |
| `--json` | | Output page metadata as JSON |
| `--help` | `-h` | Standard winix help |
| `--version` | `-V` | Standard winix version |

### Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Page found and displayed |
| 1 | Page not found |
| 2 | Usage error (bad arguments) |
| 125 | Internal tool error |
| 126 | Cannot execute pager |
| 127 | Required dependency not found |

### JSON Output

```json
{
  "tool": "man",
  "version": "0.5.0",
  "exit_code": 0,
  "name": "timeit",
  "section": 1,
  "title": "TIMEIT",
  "description": "time command execution with resource usage",
  "source": "Winix",
  "date": "2026-03-31",
  "file_path": "/usr/local/share/man/man1/timeit.1"
}
```

---

## Authoring Pipeline

### Source Format

Man pages for winix tools are authored in Markdown, following a prescribed structure that maps to standard man page sections:

```markdown
# timeit(1) — time command execution

## SYNOPSIS

**timeit** [*options*] [**--**] *command* [*args*...]

## DESCRIPTION

Runs *command* and reports wall-clock time, CPU time, peak memory,
and exit code.

## OPTIONS

**-j**, **--json**
: Output results as JSON to stdout.

**-q**, **--quiet**
: Suppress the summary; only run the command.

## EXIT CODES

...

## SEE ALSO

**peep**(1), **time**(1)
```

### Build-Time Conversion

`pandoc` converts Markdown to groff at build time:

```bash
pandoc -s -t man src/timeit/timeit.1.md -o src/timeit/man/man1/timeit.1
```

This runs as a build step (MSBuild target or script). The generated `.1` files are committed to the repo (so builds don't require pandoc) but regenerated when the Markdown source changes.

### Shipping

Generated groff files are included in:
- **NuGet tool packages** — bundled in a `man/` directory inside the package
- **Standalone native binaries** — placed alongside the binary in `man/man1/`
- **Multi-call binary** — all tool pages bundled in `man/man{section}/`

---

## Testing Strategy

### Lexer Tests
- Tokenise known groff snippets, verify token types and content
- Test inline escape parsing (`\fB`, `\-`, `\(em`, etc.)
- Test edge cases: empty lines, consecutive requests, text with embedded backslashes

### MacroExpander Tests
- Feed token streams for each supported macro, verify IR output
- Test `.TP` (tagged paragraph) state machine — tag line followed by body
- Test `.RS`/`.RE` indent nesting
- Test font alternation macros (`.BR`, `.BI`, etc.)
- Test auto-detection of `man` vs `mdoc` format

### TerminalRenderer Tests
- Render IR blocks to string, verify ANSI output
- Test width wrapping with long lines
- Test that colour/hyperlink output is absent when `ConsoleEnv` reports no support
- Test `TitleBlock` header centering

### PageDiscovery Tests
- Use temp directories with known man page hierarchies
- Test section preference (section 1 preferred over section 3)
- Test `.gz` file discovery
- Test `MANPATH` parsing (colon and semicolon separators)

### Integration Tests
- Round-trip: groff source → lexer → expander → renderer → verify output contains expected text
- Test with real man pages from Git for Windows (if available in CI)

---

## Phases

### Phase 1 — MVP
- `man` macro lexer and expander
- Terminal renderer with colour and OSC 8
- Page discovery (bundled + `MANPATH` + auto-detect)
- Pager chain with built-in minimal pager
- `.gz` decompression
- CLI interface (all flags except `-k`)
- Markdown → groff build pipeline for winix tool pages
- Man pages authored for all existing winix tools (timeit, peep, squeeze, wargs)

### Phase 2 — `mdoc` Support
- `mdoc` macro expander (same IR, same renderer)
- Full macOS man page compatibility

### Phase 3 — Indexing
- `man -k` / `apropos` keyword search
- `whatis` database generation (`man --update-index`)
- Index stored per search path entry

---

## Non-Goals

- Full groff language support (conditionals, string registers, diversions, traps)
- PDF/PostScript output
- `catman` pre-formatted page caching
- Building a `less` replacement (separate tool, separate brainstorm)
- A Markdown-to-groff converter as a standalone tool (may come with ShellKit)
- Editing or authoring man pages (this tool is read-only)
