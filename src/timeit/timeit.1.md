% TIMEIT(1) Winix | User Commands
% Troy Willmot
% 2026-04-11

# NAME

timeit - time command execution

# SYNOPSIS

**timeit** [*options*] [**--**] *command* [*args*...]

# DESCRIPTION

Runs *command* and reports wall-clock time, CPU time, peak memory usage, and exit code.

A transparent wrapper — the child's stdout, stderr, and exit code pass through unmodified. Summary output goes to stderr by default, so it does not pollute piped command output.

# OPTIONS

**-1**, **--oneline**
:   Single-line output format.

**--json**
:   Output results as JSON to stderr.

**--stdout**
:   Write the summary to stdout instead of stderr.

**--color**
:   Force coloured output (overrides **NO_COLOR**).

**--no-color**
:   Disable coloured output.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

# OUTPUT FORMATS

**Default** (multi-line, stderr):

    real  12.4s
    user  9.1s
    sys   0.300s
    peak  482.0 MB
    exit  0

**One-line** (**-1** / **--oneline**):

    [timeit] 12.4s wall | 9.1s user | 0.300s sys | 482.0 MB peak | exit 0

**JSON** (**--json**):

    {"tool":"timeit","exit_code":0,"wall_seconds":12.400,...}

# EXIT CODES

**0**
:   Child process exited 0.

**1-124**
:   Child process exit code (passed through).

**125**
:   Usage error — no command specified or bad timeit arguments.

**126**
:   Command found but not executable.

**127**
:   Command not found.

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# EXAMPLES

    timeit dotnet build

    timeit --json dotnet test

    timeit -1 dotnet publish -c Release

    timeit --color dotnet build 2>&1 | tee build.log

    timeit -- myapp --help

# SEE ALSO

**squeeze**(1), **peep**(1), **wargs**(1), **files**(1), **treex**(1), **man**(1)
