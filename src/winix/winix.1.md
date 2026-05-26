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
:   List all available Winix tools. With **--json**, emits a JSON envelope on stdout instead of the human-readable table.

**status**
:   Show install status and version of each tool. With **--json**, emits a JSON summary envelope on stdout instead of the one-line text summary.

# OPTIONS

**--via** *PM*
:   Force a specific package manager: **scoop**, **winget**, **dotnet**, **brew**.

**--dry-run**
:   Print what would be done without executing any changes.

**--json**
:   For **list** and **status**, emit a JSON envelope on stdout (suite convention) rather than the human-readable table or summary. The envelope includes the winix version, current platform, and per-tool install state; pipe through **jq** for scripted consumption. Ignored on **install**, **update**, and **uninstall**.

**--describe**
:   Print machine-readable metadata and exit.

**--color**
:   Force coloured output, overriding **NO_COLOR**.

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
