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
:   Manage the marker-delimited Winix discoverability pointer. By default it operates on your per-user agent config (**~/.claude/CLAUDE.md**, **~/.codex/AGENTS.md**) — run once per machine; the block is never committed to a repository. With **--project** it operates on a repo's committed **AGENTS.md**/**CLAUDE.md** using conditional ("if available") wording instead. *verb* is one of **init**, **status**, or **remove**.

:   **init** — write or refresh the managed block. In user scope, writes every known agent home whose directory exists (or is force-created with **--claude**/**--codex**); with no home and no force flag it writes nothing and exits 125. With **--project**, **AGENTS.md** is always a target and **CLAUDE.md** is added when it exists or **--claude** is given. The block embeds a version-pinned URL; re-running at the same version is byte-stable. Supports **--project**, **--path**, **--claude**, **--codex**, **--dry-run**, **--json**.

:   **status** — report whether the block is present and current. Exits 0 if current, 1 if absent or stale in any applicable file (or, in user scope, if no agent home exists). Supports **--project**, **--path**, **--claude**, **--codex**, **--json**.

:   **remove** — strip the managed block from all applicable files. Supports **--project**, **--path**, **--claude**, **--codex**, **--dry-run**, **--json**.

# OPTIONS

**--via** *PM*
:   Force a specific package manager: **scoop**, **winget**, **dotnet**, **brew**.

**--project**
:   For **agents**: write into committed project files (**AGENTS.md**/**CLAUDE.md**) instead of user/global agent config.

**--path** *DIR*
:   For **agents**: project directory for **--project** (default: current directory). Only valid with **--project**.

**--claude**
:   For **agents**: force the Claude home/file even when absent — user scope creates **~/.claude/CLAUDE.md**; **--project** includes **CLAUDE.md**.

**--codex**
:   For **agents**: force the Codex user home (**~/.codex/AGENTS.md**) even when absent. User scope only (cannot combine with **--project**).

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
:   One or more tools failed to install, update, or uninstall; or (agents) the pointer block is absent or stale, or (user scope) no agent home exists.

**125**
:   Usage error (bad arguments or unrecognised command).

**126**
:   Cannot execute — no supported package manager found.

**127**
:   Internal error (manifest fetch/parse failure, or agents file I/O failure).

For the **agents** subcommand, exit 1 means the discoverability block is absent or stale (use
**winix agents init** to fix); 125 means bad arguments — including **--path** without **--project**,
**--project** with **--codex**, a project path that is not a directory, or a user-scope **init** with
no agent home and no **--claude**/**--codex** force flag.

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

**WINIX_AGENTS_HOME**
:   For **agents** (test/smoke isolation only): an absolute path used in place of the OS user-profile directory when resolving user-scope agent homes. Lets tests and smoke runs redirect writes to a scratch directory instead of the real **~/.claude**. May point at a non-existent directory.

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

    winix agents init --codex

    winix agents init --project --path /path/to/project --claude --dry-run

# SEE ALSO

**timeit**(1), **squeeze**(1), **peep**(1), **wargs**(1), **files**(1), **treex**(1), **man**(1)
