% RETRY(1) Winix | User Commands
% Troy Willmot
% 2026-04-23

# NAME

retry - run a command with automatic retries on failure

# SYNOPSIS

**retry** [*options*] [**--**] *command* [*args*...]

# DESCRIPTION

Runs *command* and automatically retries it on failure. Supports configurable retry count, delay, backoff strategy, jitter, and exit-code filtering.

A transparent wrapper — the child's stdout, stderr, and exit code pass through unmodified on the final attempt. Summary output goes to stderr by default, so it does not pollute piped command output.

# OPTIONS

**-n** *N*, **--times** *N*
:   Maximum number of retry attempts, not counting the initial run. Default: 3.

**-d** *D*, **--delay** *D*
:   Delay before each retry. Accepts a number followed by a unit suffix: **ms** (milliseconds), **s** (seconds), **m** (minutes). Default: **1s**.

**-b** *S*, **--backoff** *S*
:   Backoff strategy. One of **fixed** (same delay every retry), **linear** (delay increases by the base delay each retry), or **exp** (delay doubles each retry). Default: **fixed**.

**--jitter**
:   Add random jitter: the computed delay is multiplied by a random factor between 0.5 and 1.0. Useful when multiple processes may retry simultaneously.

**--on** *X*[**,**...]
:   Retry only when the exit code matches one of the given comma-separated values. Any other exit code is passed through immediately without retrying.

**--until** *X*[**,**...]
:   Stop retrying when the exit code matches one of the given comma-separated values (poll mode). Retries continue until the command exits with one of these codes or attempts are exhausted. The default **--times** 3 still applies — set **--times** explicitly to the desired wait budget when polling (total wait is roughly **--times** × **--delay**).

**--stdout**
:   Write the success summary to stdout instead of stderr. Errors (usage, runtime failures) always go to stderr regardless of this flag — pipe consumers expect stdout to be clean on failure.

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

# EXIT CODES

**0**
:   Child process exited 0. (A non-zero exit code that matches **--until** still passes through as that code — only the *reason* is "succeeded", not the exit code.)

**1-124**
:   Child process exit code (passed through on exhaustion or non-retryable result).

**125**
:   Usage error — no command, bad retry arguments, or empty **--on**/**--until** list (e.g. **--on \"\"** or **--on ,,,**). An empty list is rejected rather than silently disabling the filter.

**126**
:   Command found but not executable (permission denied, bad EXE format). Includes unexpected process-start failures.

**127**
:   Command not found on PATH. When a launch failure occurs mid-loop (child binary removed between attempts), the JSON summary preserves the partial history: **attempts** reflects how many succeeded before the failure, and **child_exit_code** is **null**.

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# EXAMPLES

    retry dotnet test

    retry --times 5 --delay 2s dotnet test

    retry --times 5 --delay 1s --backoff exp --jitter curl -f http://api/health

    retry --until 0 --delay 5s docker ps

    retry --until 0 --times 30 --delay 2s nc -z localhost 5432

    retry --on 1,2 --times 3 make build

    timeit retry make test

# SEE ALSO

**timeit**(1), **peep**(1), **wargs**(1), **files**(1), **squeeze**(1), **nc**(1)
