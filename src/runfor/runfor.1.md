% RUNFOR(1) Winix | User Commands
% Troy Willmot
% 2026-06-16

# NAME

runfor - run a command with a time limit

# SYNOPSIS

**runfor** [*options*] *DURATION* [**--**] *command* [*args*...]

# DESCRIPTION

Runs *command* and kills it if it has not exited within *DURATION*. A cross-platform replacement for **timeout**(1) from coreutils, with a consistent exit-code family and an optional JSON summary envelope.

*DURATION* accepts a number followed by a unit suffix: **ms** (milliseconds), **s** (seconds), **m** (minutes), **h** (hours). Examples: **500ms**, **30s**, **5m**, **1h**.

Use **--** before commands that take their own dashed flags to prevent runfor from consuming them.

The child's stdout and stderr pass through unmodified. runfor's own notice and **--json** summary go to stderr; they do not pollute piped command output.

# OPTIONS

**-s** *NAME*, **--signal** *NAME*
:   Signal sent at the deadline on Unix: TERM (default), HUP, INT, QUIT, KILL. Ignored on Windows.

**-k** *GRACE*, **--kill-after** *GRACE*
:   Unix: after the deadline signal, wait GRACE then SIGKILL the tree. No-op on Windows (kills immediately).

**--json**
:   Output results as JSON to stderr.

**--color**[=_WHEN_]
:   Coloured output: auto (default when omitted), always, or never.

**--no-color**
:   Disable coloured output.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

**--describe**
:   Emit machine-readable JSON metadata (flags, examples, composability).

# EXIT STATUS

**0–123**
:   Child exited before the deadline (exit code forwarded).

**124**
:   Deadline exceeded — the child was terminated.

**125**
:   Usage error: missing/invalid DURATION, no command, bad **--signal**/**--kill-after**.

**126**
:   Command found but not executable (permission denied, bad EXE format). Includes unexpected process-start failures.

**127**
:   Command not found on PATH.

**130**
:   Interrupted by Ctrl+C.

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# EXAMPLES

    runfor 30s -- curl https://example.com

    runfor 5m -- dotnet test

    runfor --kill-after 3s 10s -- ./server

    runfor --signal INT 1m -- ./job

# NOTES

**Windows behaviour.** runfor kills the entire process tree immediately at the deadline using **TerminateProcess**. There is no signal model on Windows; **--signal** and **--kill-after** are accepted but have no effect.

**Unix default (coreutils-faithful).** At the deadline runfor sends **--signal** (default **TERM**) to the direct child and exits 124. A child that ignores the signal survives — there is no automatic SIGKILL backstop unless **--kill-after** is also given. This matches **timeout**(1) without **-k**, so existing scripts behave identically.

**Direct-child-only signalling (ADR D10).** runfor signals only the direct child, not the full process tree. A child that handles the signal and exits within the **--kill-after** grace may leave its own grandchildren running — the SIGKILL tree-backstop only reaps the whole tree when the child ignores the signal past the grace period. For a wrapper that spawns long-lived workers, have it forward the signal to its children.

# SEE ALSO

**timeout**(1), **retry**(1)
