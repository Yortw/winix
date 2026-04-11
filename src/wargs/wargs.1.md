% WARGS(1) Winix | User Commands
% Troy Willmot
% 2026-04-11

# NAME

wargs - read items from stdin and execute a command for each one

# SYNOPSIS

*input* **|** **wargs** [*options*] [**--**] *command* [*args*...]

# DESCRIPTION

Reads items from stdin and executes *command* for each one. Line-delimited by default. If the command contains **{}**, each item replaces the placeholder; otherwise items are appended as trailing arguments.

Supports parallel execution (**-P**), batching (**-n**), dry-run, confirm, and structured output — a cross-platform **xargs** replacement with sane defaults.

With no *command*, items are echoed to stdout (like **echo**).

# OPTIONS

**-P**, **--parallel** *N*
:   Maximum concurrent jobs (default: 1; 0 = unlimited).

**-n**, **--batch** *N*
:   Items per command invocation (default: 1).

**-0**, **--null**
:   Null-delimited input (for use with **find -print0**).

**-d**, **--delimiter** *CHAR*
:   Custom single-character input delimiter.

**--compat**
:   POSIX whitespace splitting with quote handling.

**--fail-fast**
:   Stop spawning new jobs after the first child failure.

**-k**, **--keep-order**
:   Print output in input order (parallel mode only).

**--line-buffered**
:   Children inherit stdio directly (no output buffering).

**-p**, **--confirm**
:   Prompt before each job.

**--dry-run**
:   Print commands that would be executed without running them.

**-v**, **--verbose**
:   Print each command to stderr before running it.

**--json**
:   JSON summary to stderr on exit.

**--ndjson**
:   Streaming NDJSON per job to stderr.

**--no-shell-fallback**
:   Disable shell fallback for shell builtins.

**--color**
:   Force coloured output.

**--no-color**
:   Disable coloured output.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

# SHELL BUILTINS

Commands like **echo**, **del**, **type**, and **ver** are shell builtins on Windows. By default, wargs retries failed commands via the platform shell (**cmd /c** on Windows, **sh -c** on Unix), so builtins work transparently. Use **--no-shell-fallback** to require standalone executables.

# EXIT CODES

**0**
:   All jobs succeeded.

**123**
:   One or more child processes failed.

**124**
:   Aborted due to **--fail-fast**.

**125**
:   Usage error (bad arguments).

**126**
:   Command not executable.

**127**
:   Command not found.

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# EXAMPLES

    cat servers.txt | wargs ssh {} "uptime"

    find . -name "*.cs" | wargs -P4 dotnet format {}

    cat urls.txt | wargs -n10 curl

    find . -name "*.log" | wargs --dry-run rm

    find . -name "*.bak" | wargs -p rm

    git ls-files "*.cs" | wargs --json dotnet format {}

    find . -name "*.tmp" -print0 | wargs -0 rm

    cat hosts.txt | wargs -v ping -c1 {}

# SEE ALSO

**timeit**(1), **squeeze**(1), **peep**(1), **files**(1), **man**(1)
