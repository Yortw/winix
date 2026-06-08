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

**agents** *verb*
:   Manage the marker-delimited Winix discoverability pointer in a project's **AGENTS.md** (and **CLAUDE.md** when applicable). *verb* is one of **init**, **status**, or **remove**.

:   **init** — write or refresh the managed block. **AGENTS.md** is always a target; **CLAUDE.md** is added when it already exists or **--claude** is given. The block embeds a version-pinned URL; re-running at the same version is byte-stable. Supports **--path**, **--claude**, **--dry-run**, **--json**.

:   **status** — report whether the block is present and current. Exits 0 if current, 1 if absent or stale in any applicable file. Supports **--path**, **--claude**, **--json**.

:   **remove** — strip the managed block from all applicable files. Supports **--path**, **--claude**, **--dry-run**, **--json**.

# OPTIONS

**--via** *PM*
:   Force a specific package manager: **scoop**, **winget**, **dotnet**, **brew**.

**--path** *DIR*
:   For **agents**: project directory to operate on (default: current directory).

**--claude**
:   For **agents**: include **CLAUDE.md** as a target even when it does not already exist.

**--dry-run**
:   Print what would be done without executing any changes.

**--json**
:   For **list** and **status**, emit a JSON envelope on stdout (suite convention) rather than the human-readable table or summary. The envelope includes the winix version, current platform, and per-tool install state; pipe through **jq** for scripted consumption. Ignored on **install**, **update**, and **uninstall**.

**--describe**
:   Print machine-readable metadata and exit.

**--color**[=_WHEN_]
:   Coloured output: auto (default when omitted), always, or never.

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
:   Internal error (also: **agents** I/O failure).

For the **agents** subcommand, exit 1 means the discoverability block is absent or stale (use
**winix agents init** to fix); 125 means bad arguments or the path is not a directory.

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

    winix agents init

    winix agents status

    winix agents init --path /path/to/project --claude --dry-run

# SEE ALSO

**timeit**(1), **squeeze**(1), **peep**(1), **wargs**(1), **files**(1), **treex**(1), **man**(1)
