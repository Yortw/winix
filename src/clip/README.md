# clip

Cross-platform clipboard bridge. Copy from stdin, paste to stdout, clear — with identical behaviour on Windows, macOS, and Linux.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/clip
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Clip
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Clip
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
clip                     # paste clipboard contents to stdout
echo text | clip         # copy stdin to the clipboard
clip --clear             # empty the clipboard
clip -c                  # force copy mode
clip -p                  # force paste mode
clip -r                  # paste raw (preserve trailing newline)
clip --primary           # target PRIMARY selection (Linux X11/Wayland)
```

### Examples

```bash
# Copy the output of a command
date | clip

# Paste into another command
clip | grep foo

# Clear after copying a secret
read -rs SECRET
echo -n "$SECRET" | clip
# ...paste where needed...
clip --clear

# Round-trip through a file
cat report.txt | clip
clip > report-copy.txt
```

## Newline handling

> **Important.** `clip` treats newlines asymmetrically by default:
>
> - **Copy** is byte-preserving. `echo foo | clip` stores `foo\n` on the clipboard, exactly as stdin delivered it.
> - **Paste** strips exactly one trailing `\n` (or `\r\n`). `clip` then prints `foo` without the newline.
>
> Why? This matches `$(...)` shell-substitution semantics: `result=$(clip)` behaves like `result=$(cat file)` — no phantom trailing newline that breaks comparisons or concatenations.
>
> Use `--raw` / `-r` on paste to disable the stripping and get exactly what is on the clipboard:
>
> ```bash
> clip -r > out.bin     # byte-exact paste
> clip | xxd            # may appear shorter than 'clip -r | xxd'
> ```

## Git Bash on Windows (autodetect caveat)

Git Bash (MSYS/mingw) on Windows presents a non-TTY stdin to .NET processes even when run interactively. As a result, `clip` with no arguments in Git Bash may detect "stdin is redirected" and switch to copy mode — reading empty stdin and overwriting the clipboard with an empty string.

**Workaround:** use the explicit `-p` / `--paste` flag when pasting in Git Bash:

```bash
clip -p
```

`cmd.exe`, PowerShell, and Windows Terminal's native consoles do not have this quirk — bare `clip` pastes correctly there.

## Options

| Option | Description |
|---|---|
| `-c`, `--copy` | Force copy mode regardless of stdin state. |
| `-p`, `--paste` | Force paste mode regardless of stdin state. |
| `--clear` | Empty the clipboard and exit. |
| `-r`, `--raw` | Do not strip trailing newline on paste. |
| `--primary` | Target the X11/Wayland PRIMARY selection (middle-click on Linux). Silently ignored on Windows and macOS. |
| `--color WHEN` | `auto`, `always`, or `never`. Respects `NO_COLOR`. |
| `--describe` | Emit structured JSON for AI discoverability. |
| `--help` | Show help. |
| `--version` | Show version. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success (empty clipboard on paste also returns 0 with empty stdout). |
| 125 | Usage error — invalid flags, conflicting modes, or invalid UTF-8 input. |
| 126 | Clipboard busy (another Windows process holds it, or helper binary failed on Linux/macOS). |
| 127 | No clipboard helper found (Linux without `wl-clipboard`, `xclip`, or `xsel`). |

## Platform notes

- **Windows.** Native implementation via `user32.dll` P/Invoke. `clip` supersedes the built-in `System32\clip.exe` when earlier in `PATH`. Scoop installs place shims ahead of `System32` automatically. For other install methods, ensure the install location precedes `System32` in `PATH`. Old scripts that pipe into `clip` continue to work — ours is a strict superset of the Windows built-in (which is write-only).
- **macOS.** Shells out to the built-in `pbcopy` / `pbpaste`, which are always present. Text only in v1 — rich-type support (HTML, RTF, images) is planned for v2 via NSPasteboard.
- **Linux.** Auto-detects Wayland (via `WAYLAND_DISPLAY`) and prefers `wl-copy` / `wl-paste`. Falls back to `xclip`, then `xsel`. If none are installed, `clip` exits 127 with an install hint. On most desktop distributions one of these is pre-installed; on minimal server images you may need to install `wl-clipboard`, `xclip`, or `xsel`.

## Colour

`clip` has no coloured output itself. The `--color` flag is accepted for suite consistency. `NO_COLOR` is respected.

## See also

- `man clip` (after `winix install man`)
- `clip --describe` for JSON metadata
