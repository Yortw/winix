# less — AI Agent Guide

## What This Tool Does

`less` is a terminal pager that displays file content (or stdin) one screen at a time. It passes ANSI colour codes through unchanged by default, supports forward and backward search, follow mode (like `tail -f`), and exits immediately when output fits on one screen. Use it whenever a task involves displaying output interactively or when a pipeline requires a pager.

## Platform Story

Cross-platform. On **Windows**, no `less` command exists natively — Winix `less` fills that gap entirely. On **Linux/macOS**, system `less` exists but defaults are often unhelpful (colour not passed through, screen cleared on exit). Winix `less` ships with sane defaults (`FRX`) that work well without any configuration on all platforms.

## When to Use This

- Paging coloured output from other tools: `man timeit | less`
- Viewing a large file with search: `less -N largefile.log`
- Following a growing log file: `less +F app.log`
- Paging output that may or may not fit on one screen: `less -F` (exits if it fits)
- Viewing piped output that preserves ANSI formatting: `treex . | less`

## Common Patterns

**Page a file:**
```bash
less somefile.txt
```

**Page piped input with colour:**
```bash
man timeit | less
```

**Show line numbers:**
```bash
less -N somefile.txt
```

**Follow a log file:**
```bash
less +F /var/log/app.log
```

**Jump to end of file:**
```bash
less +G somefile.txt
```

**Open with an initial search:**
```bash
less +/error logfile.log
```

**Quit if output fits on one screen:**
```bash
some-command | less -F
```

## Composing with Other Tools

**man + less** — page a rendered man page with colour:
```bash
man --no-pager timeit | less
```

**files + less** — browse a large file listing:
```bash
files . --recursive | less
```

**treex + less** — scroll a deep directory tree:
```bash
treex . --depth 10 | less
```

**timeit + less** — review long command output:
```bash
timeit some-command 2>&1 | less
```

## Gotchas

**`-R` is on by default.** ANSI codes are passed through unless `NO_COLOR` is set or `--no-color` is passed. If you see raw escape sequences, check whether `NO_COLOR` is set in the environment.

**`LESS` env var replaces defaults entirely.** If `LESS` is set to any non-empty value, the built-in defaults (`FRX`) are not applied. This matches traditional `less` behaviour. Unset `LESS` or set it to empty to restore Winix defaults.

**`+F` follow mode stays active until `q` is pressed.** Unlike `tail -f`, pressing `q` in follow mode returns to normal paging rather than exiting. Press `q` a second time to quit from normal mode.

**`-F` quit-if-one-screen is on by default.** If output fits on a single screen, `less` exits without waiting for a keypress. This is part of the default `FRX` settings. Pass `-F-` (or set `LESS=RX`) to disable this if you always want interactive paging.

**Multiple files are paged in sequence.** When invoked with multiple file arguments, `less` shows them one after another. The `:n` and `:p` bindings navigate between files (next/previous).

**stdin is consumed on first open.** When reading from stdin, the content is buffered in memory. Very large stdin streams may cause high memory usage. For large file viewing, prefer passing the file path directly.

## Key Bindings Reference

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
| `F` | Enter follow mode |
| `-N` | Toggle line numbers |
| `-S` | Toggle line chopping |

## LESS Environment Variable

- **Unset or empty**: Winix `less` applies `FRX` defaults (quit-if-one-screen, raw colour passthrough, no-init screen clear)
- **Non-empty**: replaces defaults entirely — set carefully to avoid losing colour passthrough

Recommended: leave `LESS` unset and use flags explicitly when you need different behaviour.
