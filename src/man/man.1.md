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
3. **Auto-detected** system locations (**\/usr\/share\/man**, **\/usr\/local\/share\/man**, etc. on Linux/macOS; no system locations on Windows).

Use **man --manpath** to inspect the effective search path.

# OPTIONS

**--no-pager**
:   Print output directly to stdout without opening a pager.

**--color**
:   Force coloured output (overrides **NO_COLOR**).

**--no-color**
:   Disable coloured output.

**--width** *N*
:   Override output width in columns (default: terminal width).

**-w**, **--path**
:   Print the path to the man page file and exit (do not render).

**--where**
:   Alias for **--path**.

**--manpath**
:   Print the list of man page search directories and exit.

**--json**
:   Write a JSON summary to stderr on exit.

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
:   Colon-separated list of directories to search for man pages, prepended to auto-detected locations.

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
