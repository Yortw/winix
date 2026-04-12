# less

Native terminal pager with ANSI colour passthrough, search, follow mode, and modern defaults.

**`less` replacement** with sane defaults on every platform. Passes ANSI escape codes through unchanged, so coloured output from tools like `man`, `files`, and `treex` renders correctly.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/less
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Less
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Less
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
less [options] [+command] [file ...]
```

Displays file content (or stdin) one screen at a time. When no file is given, reads from stdin. Multiple files are paged in sequence.

### Examples

```bash
# Page a file
less somefile.txt

# Page piped input (colour passes through by default)
man timeit | less

# Show line numbers
less -N somefile.txt

# Chop long lines instead of wrapping
less -S somefile.txt

# Follow a growing file (like tail -f)
less +F logfile.log

# Jump to end of file on open
less +G somefile.txt

# Open with an initial search
less +/error logfile.log

# Force exit if output fits on one screen
less -F somefile.txt
```

## Options

| Option | Description |
|--------|-------------|
| `-N` | Show line numbers |
| `-S` | Chop long lines (don't wrap) |
| `-F` | Quit immediately if output fits on one screen |
| `-R` | Pass raw ANSI colour codes through (default: on) |
| `-X` | Don't clear the screen on exit |
| `-i` | Case-insensitive search (ignored if pattern has uppercase) |
| `-I` | Case-insensitive search always |
| `+F` | Start in follow mode (like `tail -f`) |
| `+G` | Jump to end of file on open |
| `+/pattern` | Start with an initial forward search for `pattern` |
| `--help` | Show help and exit |
| `--version` | Show version and exit |
| `--color` | Force coloured output (overrides `NO_COLOR`) |
| `--no-color` | Disable coloured output |

## Key Bindings

| Key | Action |
|-----|--------|
| `q` | Quit |
| `j` / Down | Scroll down one line |
| `k` / Up | Scroll up one line |
| Space / PgDn | Scroll down one screen |
| PgUp | Scroll up one screen |
| Home / `g` | Jump to beginning |
| End / `G` | Jump to end |
| `/` | Forward search |
| `?` | Backward search |
| `n` | Next search match |
| `N` | Previous search match |
| `F` | Enter follow mode (press `q` to exit) |
| `-N` | Toggle line numbers |
| `-S` | Toggle line chopping |

## LESS Environment Variable

The `LESS` environment variable controls default options:

- **Unset or empty** — Winix `less` uses modern defaults: `FRX` (quit-if-one-screen, raw colour, no-init). This gives sensible out-of-the-box behaviour without configuring anything.
- **Set to a non-empty value** — replaces the defaults entirely. The value is parsed as a list of options (e.g. `LESS=-NiR`). Set `LESS=` explicitly to an empty string to disable all defaults.

This matches the behaviour of traditional `less` implementations.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Normal exit |
| 1 | Error (file not found, read error) |
| 2 | Usage error (bad arguments) |

## Colour

- ANSI escape codes are passed through by default (`-R` is on unless `NO_COLOR` is set)
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` disables colour passthrough; raw escape sequences are shown as text
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`less` is part of the [Winix](../../README.md) CLI toolkit.
