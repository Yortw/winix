% WINIX(1) Winix | User Commands
% Troy Willmot
% 2026-04-11

# NAME

winix - install, update, and uninstall Winix tools

# SYNOPSIS

**winix** *command* [*tools*...] [*options*]

# DESCRIPTION

Cross-platform installer for the Winix CLI tool suite. Installs, updates, and uninstalls all Winix tools by delegating to your platform's native package manager (Scoop or Winget on Windows; Homebrew or direct download on macOS/Linux).

# COMMANDS

**install** [*tools*...]
:   Install all Winix tools, or a named subset.

**update**
:   Update all installed Winix tools.

**uninstall** [*tools*...]
:   Uninstall all Winix tools, or a named subset.

**list**
:   List all available Winix tools.

**status**
:   Show install status and version of each tool.

# OPTIONS

**--via** *PM*
:   Force a specific package manager: **scoop**, **winget**, **dotnet**, **brew**.

**--dry-run**
:   Print what would be done without executing any changes.

**--describe**
:   Print machine-readable metadata and exit.

**--no-color**
:   Disable coloured output.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

# EXIT CODES

**0**
:   Success — all requested operations completed.

**1**
:   One or more tools failed to install, update, or uninstall.

**125**
:   Usage error (bad arguments or unrecognised command).

**126**
:   Cannot execute — no supported package manager found.

**127**
:   Internal error.

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# EXAMPLES

    winix install

    winix install timeit peep

    winix update

    winix uninstall

    winix list

    winix status

    winix install --via scoop

    winix install --dry-run

# SEE ALSO

**timeit**(1), **squeeze**(1), **peep**(1), **wargs**(1), **files**(1), **treex**(1), **man**(1)
