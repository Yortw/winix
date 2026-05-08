% MAN(1) Winix | User Commands
% Troy Willmot
% 2026-04-11

# NAME

man - display man pages

# SYNOPSIS

**man** [*options*] [*section*] *name*

# DESCRIPTION

Locates and renders a man page for *name*. Supports coloured output, clickable hyperlinks (OSC 8), and an optional pager.

Man pages are searched in the following order:

1. **Bundled** pages shipped with Winix tools (always available, cross-platform).
2. **MANPATH** — directories listed in the **MANPATH** environment variable.
3. **Auto-detected** system locations (`/usr/share/man`, `/usr/local/share/man`, etc. on Linux/macOS; no system locations on Windows).

Use **man --manpath** to inspect the effective search path.

# OPTIONS

**--no-pager**
:   Print output directly to stdout without opening a pager.

**--color**
:   Force coloured output (overrides **NO_COLOR**).

**--no-color**
:   Disable coloured output.

**--width** *N*
:   Override output width in columns. *N* must be at least 10. When omitted, width is taken from MANWIDTH if set, otherwise from the terminal width capped at 80 columns. The 80-column cap matches the effective behaviour of GNU man-db (which delegates rendering to groff, whose default width is 80) — set MANWIDTH or pass **--width** explicitly to render wider.

**-w**, **--path**
:   Print the path to the man page file and exit (do not render). The path is reported on the first match that passes a structural-validity check; the file is not opened for full read, so a path that resolves successfully here may still fail to render with **--no-pager** if the content is corrupt later in the read pipeline.

**--where**
:   Alias for **--path**.

**--manpath**
:   Print the list of man page search directories and exit.

**--json**
:   Write a JSON summary of the page metadata to stdout and exit.

**--describe**
:   Print machine-readable metadata and exit.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

# EXIT CODES

**0**
:   Man page found and rendered (or **--path** / **--manpath** output printed).

**1**
:   Man page not found.

**2**
:   Usage error (bad arguments).

**125**
:   Internal error.

# ENVIRONMENT

**MANPATH**
:   Platform-separated list of directories to search for man pages, prepended to auto-detected locations. The separator is **:** (colon) on Linux/macOS and **;** (semicolon) on Windows, matching the platform's <c>PATH</c> separator.

**MANWIDTH**
:   When set to a positive integer ≥ 10 and **--width** is not given, used as the rendering width. Otherwise the default applies (terminal width, capped at 80).

**MANPAGER**
:   The pager command to invoke for displaying rendered output. May be a bare executable (**less**) or a full command line with arguments (**less -R**, **less -R --ignore-case**); the value is passed to **/bin/sh -c** on Linux/macOS and **cmd /c** on Windows so any shell-syntax is honoured. Takes priority over **PAGER**.

**PAGER**
:   Fallback pager command, consulted when **MANPAGER** is unset. Same parsing rules as **MANPAGER**. If neither variable is set, man falls back (in order) to a sibling **less** binary in the same directory, the **less** found on **PATH**, or a built-in minimal pager.

**NO_COLOR**
:   If set, disables coloured output and clickable hyperlinks (no-color.org).

# EXAMPLES

    man timeit

    man 3 printf

    man --path timeit

    man --manpath

    man --no-pager timeit

    man --color timeit | cat

    man --json timeit

# SEE ALSO

**timeit**(1), **squeeze**(1), **peep**(1), **wargs**(1), **files**(1), **treex**(1), **winix**(1)
