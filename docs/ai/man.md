# man — AI Agent Guide

## What This Tool Does

`man` locates and renders groff man pages with colour, hyperlinks, and pager support. It searches bundled pages (shipped with Winix tools), directories in the `MANPATH` environment variable, and auto-detected system locations. Use it whenever a task involves reading documentation for a CLI tool or POSIX function.

## Platform Story

Cross-platform. On **Windows**, no `man` command exists natively — `man` fills that gap entirely, including bundled pages for all Winix tools. On **Linux/macOS**, system `man` exists but lacks colour hyperlinks, structured JSON output, and consistent cross-platform behaviour. On either platform, `man` gives a consistent, modern experience with Winix tool documentation always available.

## When to Use This

- Reading documentation for a Winix tool: `man timeit`
- Looking up a POSIX function reference: `man 3 printf`
- Checking where a man page lives on disk: `man --path timeit`
- Inspecting the effective man page search path: `man --manpath`
- Piping rendered page text to another tool: `man --no-pager timeit | grep exit`

## Common Patterns

**Read the man page for a Winix tool:**
```bash
man timeit
```

**Look up a specific section:**
```bash
man 3 printf
```

**Find the file path without rendering:**
```bash
man --path timeit
```

**Show all search directories:**
```bash
man --manpath
```

**Render to stdout (no pager, for piping):**
```bash
man --no-pager timeit
```

**Get JSON metadata about the page lookup:**
```bash
man --json timeit > meta.json
```

## Composing with Other Tools

**man + grep** — search page content without a pager:
```bash
man --no-pager timeit | grep -i exit
```

**man + peep** — live-refresh a page while editing its source:
```bash
peep -- man --no-pager timeit
```

**man --path + files** — find all man pages in a directory tree:
```bash
files /usr/share/man --ext 1 --ext 3
```

## Gotchas

**Section numbers must precede the page name.** `man 3 printf` is correct; `man printf 3` treats `printf` as the page name and `3` as a second positional argument, which will produce a usage error.

**--path prints the first match only.** If multiple search roots contain a page for the same name and section, only the path of the first match is printed — the same one that would be rendered. Use `--manpath` to see all search roots.

**--no-pager is required for piping.** When stdout is a terminal, `man` opens a pager (e.g. `less`). When stdout is redirected (pipe or file), paging is skipped automatically. If you are constructing a command to run non-interactively, pass `--no-pager` explicitly to avoid relying on pipe detection.

**MANPATH augments — does not replace — auto-detected system directories.** Search order is: bundled pages first, then MANPATH entries, then platform-detected well-known locations (`/usr/share/man`, `/usr/local/share/man`, etc. on Linux/macOS). MANPATH adds extra search roots but doesn't suppress system roots. To shadow a system page, ensure your MANPATH entry contains a same-named page in the same section — the first match wins.

**Bundled pages are read-only.** Pages bundled with Winix are embedded in the binary and cannot be updated or removed without reinstalling. They always document the installed version of the tool.

**Width defaults to `min(terminal, 80)`.** Width resolution is: `--width N` (explicit) > `MANWIDTH` env > `min(terminal_width, 80)`. The 80-column cap matches GNU man-db's effective behaviour (groff's default is 80). Pass `--width N` or set `MANWIDTH=N` to render wider in scripts that need it.

## Getting Structured Data

`man` supports two machine-readable output modes:

**JSON summary (to stdout)** — page metadata in lieu of rendering. `--json` does not render the page; it emits the lookup result and exits.
```bash
man --json timeit > meta.json
```

Fields emitted: `tool`, `version`, `name`, `section`, `path`, `description`. Output is line-terminated JSON to stdout, exit 0 on hit, exit 1 on not-found, exit 125 on internal error (e.g. corrupt gzip).

**--describe** — machine-readable flag reference and metadata (flags, types, defaults, examples, composability):
```bash
man --describe
```

Output is JSON to stdout and always exits 0. Use this when you need to verify exact flag names and types before constructing a command.
