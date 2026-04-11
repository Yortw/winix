% PEEP(1) Winix | User Commands
% Troy Willmot
% 2026-04-11

# NAME

peep - run a command repeatedly on a refreshing screen

# SYNOPSIS

**peep** [*options*] [**--**] *command* [*args*...]

# DESCRIPTION

Runs *command* repeatedly and displays its output on a refreshing full-screen display. Supports interval polling, file-watch triggers, diff highlighting, time-machine history, and auto-exit conditions.

While running, peep responds to keyboard input (see INTERACTIVE CONTROLS). Press **q** or **Ctrl+C** to quit.

# OPTIONS

**-n**, **--interval** *N*
:   Seconds between runs (default: 2).

**-w**, **--watch** *GLOB*
:   Re-run on file changes matching glob pattern (repeatable). If **-w** is given without **-n**, interval polling is disabled.

**--debounce** *N*
:   Milliseconds to debounce file change events (default: 300).

**--history** *N*
:   Maximum history snapshots to retain (default: 1000; 0 = unlimited).

**-g**, **--exit-on-change**
:   Exit when output changes between runs.

**--exit-on-success**
:   Exit when the command returns exit code 0.

**-e**, **--exit-on-error**
:   Exit when the command returns a non-zero exit code.

**--exit-on-match** *PAT*
:   Exit when output matches regex *PAT* (repeatable).

**-d**, **--differences**
:   Highlight changed lines between runs.

**--no-gitignore**
:   Disable automatic .gitignore filtering for **--watch**.

**--once**
:   Run once, display output, and exit.

**-t**, **--no-header**
:   Hide the header lines.

**--json**
:   JSON summary to stderr on exit.

**--json-output**
:   Include the last captured output in JSON (implies **--json**).

**--color**
:   Force coloured output.

**--no-color**
:   Disable coloured output.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

# INTERACTIVE CONTROLS

| Key              | Action                               |
|------------------|--------------------------------------|
| q / Ctrl+C       | Quit                                 |
| Space            | Pause or unpause display             |
| r / Enter        | Force immediate re-run               |
| d                | Toggle diff highlighting             |
| Up / Down        | Scroll while paused                  |
| PgUp / PgDn      | Scroll by page                       |
| Left / Right     | Time travel (older / newer snapshot) |
| t                | History overlay                      |
| ?                | Show or hide help overlay            |
| Escape           | Exit time-machine or close overlay   |

# TIME MACHINE

Press **Left** to enter time-machine mode, browsing historical snapshots of command output. Use **Left** / **Right** to navigate, **t** for an overview, **Enter** to jump to a snapshot, and **Space** or **Escape** to return to live mode.

# EXIT CODES

**0**
:   Auto-exit condition met, or manual quit with last child exit 0.

*N*
:   Last child process exit code (manual quit).

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

    peep git status

    peep -n 5 df -h

    peep -w "src/**/*.cs" dotnet test

    peep -d kubectl get pods

    peep --exit-on-success -- dotnet build

    peep --exit-on-match "READY" -- kubectl get pods

    peep --once -- docker ps

# SEE ALSO

**timeit**(1), **squeeze**(1), **wargs**(1), **files**(1), **man**(1)
