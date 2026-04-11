# man

Cross-platform man page viewer with colour, clickable hyperlinks, and pager support. Renders groff man pages natively on any platform.

**`man` replacement** (and works on Windows too, where no `man` command exists).

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/man
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Man
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Man
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
man [options] [section] <name>
```

Locates and renders a man page for the specified command or topic. Searches bundled pages first, then `MANPATH`, then system-detected man page locations.

### Examples

```bash
# View a man page
man timeit

# View a man page from a specific section
man 3 printf

# Show the path to the man page file instead of rendering it
man --path timeit

# Show all search directories in order
man --manpath

# Render without opening a pager
man --no-pager timeit

# Force colour output in a pipe
man --color timeit | cat

# Get machine-readable page metadata as JSON
man --json timeit

# AI agent metadata
man --describe
```

## Options

| Option | Description |
|--------|-------------|
| `--no-pager` | Print output directly to stdout instead of opening a pager |
| `--color` | Force coloured output (overrides `NO_COLOR`) |
| `--no-color` | Disable coloured output |
| `--width N` | Override output width in columns (default: terminal width) |
| `-w`, `--path` | Print the path to the man page file and exit (do not render) |
| `--where` | Alias for `--path` |
| `--manpath` | Print the list of man page search directories and exit |
| `--json` | Write a JSON summary to stderr on exit |
| `--describe` | Print machine-readable metadata (flags, examples, composability) and exit |
| `--version` | Show version |
| `-h`, `--help` | Show help |

## Page Discovery

Man pages are located in the following order:

1. **Bundled** — pages shipped with Winix tools (always available, cross-platform)
2. **`MANPATH`** — directories listed in the `MANPATH` environment variable
3. **Auto-detected** — standard system locations (`/usr/share/man`, `/usr/local/share/man`, etc. on Linux/macOS; no system locations on Windows)

Section numbers work as on POSIX: `man 3 printf` searches section 3 directories (`man3/`) within each search root.

Use `man --manpath` to inspect the effective search path.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Man page found and rendered (or `--path`/`--manpath` output printed) |
| 1 | Man page not found |
| 2 | Usage error (bad arguments) |
| 125 | Internal error |

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))
- Section headings, bold, and underline formatting from the man page source are rendered using ANSI escape codes when colour is active
- Clickable hyperlinks (OSC 8) are emitted for URLs found in the page when colour is active

## Part of Winix

`man` is part of the [Winix](../../README.md) CLI toolkit.
