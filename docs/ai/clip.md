# clip — AI Agent Guide

## What This Tool Does

`clip` is a cross-platform clipboard bridge. It copies from stdin into the system clipboard and pastes from the clipboard to stdout, with identical behaviour on Windows, macOS, and Linux. It also supports `--clear` to empty the clipboard.

## Platform Story

- **Windows:** no native paste command exists (`System32\clip.exe` is write-only). `clip` fills that gap natively via `user32.dll`.
- **Linux:** unifies the fragmented helper landscape (`xclip`, `xsel`, `wl-copy`/`wl-paste`). Autodetects Wayland vs. X11. Requires one of the helpers to be installed.
- **macOS:** shells out to the built-in `pbcopy`/`pbpaste` with the same CLI surface.

## When to Use This

- Piping command output into the clipboard: `date | clip`
- Piping clipboard contents into another command: `clip | grep foo`
- Clearing the clipboard after handling a secret: `clip --clear`
- Writing cross-platform shell scripts that need clipboard interaction

Prefer `clip` over per-platform helpers in scripts that need to run on more than one OS — the flag surface and behaviour are stable.

## Common Patterns

**Paste:**
```bash
clip
```

**Copy:**
```bash
echo hello | clip
```

**Clear:**
```bash
clip --clear
```

**Force a mode despite stdin state:**
```bash
clip -p    # force paste
clip -c    # force copy
```

**Byte-exact paste (preserve trailing newline):**
```bash
clip -r
```

## Newline Handling

`clip` is asymmetric on newlines by default: copy preserves bytes exactly, paste strips one trailing `\n` or `\r\n` to match `$(...)` shell-substitution semantics. Use `--raw`/`-r` to disable the strip and get byte-exact paste output.

## Mode auto-detection

The unified rule: **with no input, `clip` pastes; with content on stdin, `clip` copies.** This holds across every supported environment — interactive TTY, redirected pipe with content, redirected pipe with no content (Git Bash / MSYS / Cygwin where stdin reports redirected even interactively, or an explicit empty pipe like `: | clip`):

- Terminal stdin → paste
- Redirected stdin with content → copy
- Redirected stdin that is empty → paste

Explicit `-c` / `-p` / `--clear` always override.

**Scripting determinism.** When a producer command may or may not output content and you want guaranteed copy behaviour, use `clip --copy` (or `-c`) explicitly:

```bash
producer | clip --copy     # always copies, even if 'producer' outputs nothing
producer | clip --paste    # always pastes, ignoring stdin entirely
```

**Empty-copy edge:** to deliberately copy an empty string use `clip --copy < /dev/null`. `echo -n "" | clip` (no `--copy`) pastes instead. `clip --clear` is usually what users mean for "empty the clipboard".

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. Empty clipboard on paste also returns 0. |
| 125 | Usage error — invalid flags, conflicting modes, invalid UTF-8 stdin. |
| 126 | Clipboard busy (Windows) or helper binary failed. |
| 127 | No clipboard helper found on Linux. |

## Composability

- `ids | clip` — copy a generated ID
- `digest sha256 file | clip` — copy a hash
- `qr ... | clip` — copy a QR payload
- Round-trip: `cat file | clip && clip > copy.txt`

## Flags

Run `clip --describe` for structured metadata or `clip --help` for human help.
