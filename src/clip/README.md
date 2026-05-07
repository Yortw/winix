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

## Mode auto-detection

The unified rule: **with no input, `clip` pastes; with content on stdin, `clip` copies.** This holds across every supported environment — interactive TTY, redirected pipe with content, redirected pipe with no content (e.g. Git Bash / MSYS / Cygwin where stdin reports redirected even interactively, or an explicit empty pipe like `: | clip`):

- Bare `clip` with a terminal stdin → **paste**
- Bare `clip` with redirected stdin that has content → **copy**
- Bare `clip` with redirected stdin that is empty → **paste**

Explicit `-c` / `-p` / `--clear` always override auto-detection.

### Scripting tip

The autodetect "no content = paste" rule is friendly for interactive use but ambiguous in scripts where a producer command may or may not output content. If your pipeline can produce empty output and you want **deterministic copy** behaviour:

```bash
producer | clip --copy     # always copies, even if 'producer' outputs nothing
producer | clip --paste    # always pastes, ignoring stdin entirely
```

The explicit flag is the escape hatch — it bypasses the autodetect entirely. Use `clip --copy < /dev/null` (or `clip -c < /dev/null`) when you specifically want to copy an empty string. `clip --clear` is the more idiomatic way to empty the clipboard.

## Unicode and `cmd.exe` on Windows

`clip` stores text on the clipboard as UTF-16 (via `CF_UNICODETEXT`) and reads/writes stdin/stdout as UTF-8. Non-ASCII content (emoji, CJK, accented letters) round-trips correctly when the shell and terminal agree on UTF-8.

Legacy `cmd.exe` defaults to an OEM code page (usually 437 or 850). In that mode:

- **Copy via pipe:** `echo 🌏 | clip` does **not** work — `echo` itself replaces the emoji with `?` **before** the bytes reach `clip`. Confirm with `echo 🌏 > test.txt; type test.txt` (no clip involved).
- **Paste to terminal:** `clip` writes UTF-8 bytes to stdout; `clip` also asks the console to switch to code page 65001 so those bytes render correctly. Older tooling that hasn't followed this may still mojibake, but the clipboard is correct.

**Fixes for cmd.exe emoji round-trip:**

```
chcp 65001
echo 🌏 | clip.exe
clip.exe
```

Or use PowerShell 7+, Windows Terminal, or Git Bash — all three default to UTF-8. (Git Bash's stdin auto-detection is described in the **Mode auto-detection** section above.)

Copying via Ctrl+C from any GUI app (browser, Word, VS Code) into `clip` via paste always works — no shell is involved.

## Options

| Option | Description |
|---|---|
| `-c`, `--copy` | Force copy mode (read stdin to clipboard), overriding autodetect. |
| `-p`, `--paste` | Force paste mode (read clipboard to stdout), overriding autodetect. |
| `--clear` | Empty the clipboard, overriding autodetect. |
| `-r`, `--raw` | Do not strip trailing newline on paste. |
| `--primary` | Target X11/Wayland PRIMARY selection (Linux only; ignored elsewhere). The PRIMARY selection is the X11 middle-click buffer. Applies to copy, paste, and clear — `cat foo | clip --primary` copies to PRIMARY, `clip --primary` pastes from it, `clip --primary --clear` clears it. |
| `--color`, `--no-color` | Respect `NO_COLOR`. `clip` has no coloured output of its own; flags are accepted for suite consistency. |
| `--json` | Accepted for suite consistency. `clip` emits no records, so `--json` is a no-op. |
| `--describe` | Emit structured JSON for AI discoverability. |
| `-h`, `--help` | Show help. |
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
