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
man --json timeit 2>meta.json
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

**MANPATH overrides auto-detection.** When `MANPATH` is set, auto-detected system directories are not searched. This matches the behaviour of traditional `man` implementations. If you need both, include system paths explicitly in `MANPATH`.

**Bundled pages are read-only.** Pages bundled with Winix are embedded in the binary and cannot be updated or removed without reinstalling. They always document the installed version of the tool.

**Width defaults to terminal width.** When stdout is not a terminal (pipe, redirect), width defaults to 80 columns. Pass `--width N` to override in scripts that expect a specific line length.

## Getting Structured Data

`man` supports two machine-readable output modes:

**JSON summary (to stderr)** — page lookup result and metadata after the render completes:
```bash
man --json timeit 2>meta.json
```

Fields include: `tool`, `version`, `exit_code`, `exit_reason`, `name`, `section`, `path`, `title`.

**--describe** — machine-readable flag reference and metadata (flags, types, defaults, examples, composability):
```bash
man --describe
```

Output is JSON to stdout and always exits 0. Use this when you need to verify exact flag names and types before constructing a command.
