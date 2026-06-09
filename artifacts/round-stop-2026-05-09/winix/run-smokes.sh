#!/usr/bin/env bash
# Baseline-suite runner for winix tool. winix actually changes machine state, so
# every install/update/uninstall smoke uses --dry-run.

set -u

ART="d:/projects/winix/artifacts/round-stop-2026-05-09/winix"
WINIX_EXE="$ART/fresh-publish/winix.exe"
RES="$ART/results"
mkdir -p "$RES"

if [ ! -x "$WINIX_EXE" ]; then
  echo "FATAL: $WINIX_EXE not found" >&2
  exit 1
fi

# Generous timeout because winix list hits the network.
SMOKE_TIMEOUT=60

smoke() {
  local id="$1" ; shift
  local desc="$1" ; shift
  if [ "$1" != "--" ]; then echo "missing -- separator" >&2; return 2; fi
  shift
  echo "[$id] $desc"
  timeout "${SMOKE_TIMEOUT}s" "$@" > "$RES/$id.stdout.txt" 2> "$RES/$id.stderr.txt"
  echo "$?" > "$RES/$id.exit.txt"
}

smoke_env() {
  local id="$1" ; shift
  local desc="$1" ; shift
  local envvars="$1" ; shift
  if [ "$1" != "--" ]; then echo "missing -- separator" >&2; return 2; fi
  shift
  echo "[$id] $desc (env: $envvars)"
  timeout "${SMOKE_TIMEOUT}s" env $envvars "$@" > "$RES/$id.stdout.txt" 2> "$RES/$id.stderr.txt"
  echo "$?" > "$RES/$id.exit.txt"
}

# ----- B: Standard baselines -----

smoke B01 "--version" -- "$WINIX_EXE" --version
smoke B02 "--help" -- "$WINIX_EXE" --help
smoke B03 "--describe" -- "$WINIX_EXE" --describe
smoke B04 "no args" -- "$WINIX_EXE"
smoke B05 "unknown flag" -- "$WINIX_EXE" --invalid-flag
smoke B06 "winix list (network req)" -- "$WINIX_EXE" list
smoke_env B07 "NO_COLOR + list" "NO_COLOR=1" -- "$WINIX_EXE" list
smoke B08 "list --json" -- "$WINIX_EXE" list --json
smoke B09 "list --color" -- "$WINIX_EXE" --color list
smoke B10 "list --no-color" -- "$WINIX_EXE" --no-color list
smoke B11 "list with extra positional" -- "$WINIX_EXE" list extra-arg
smoke B12 "install --dry-run captured" -- "$WINIX_EXE" install --dry-run

# ----- C: Command dispatch -----

smoke C01 "install --dry-run" -- "$WINIX_EXE" install --dry-run
smoke C02 "install timeit --dry-run" -- "$WINIX_EXE" install timeit --dry-run
smoke C03 "install nonexistent --dry-run" -- "$WINIX_EXE" install nonexistent-tool-xyz --dry-run
smoke C04 "update --dry-run" -- "$WINIX_EXE" update --dry-run
smoke C05 "uninstall --dry-run" -- "$WINIX_EXE" uninstall --dry-run
smoke C06 "list" -- "$WINIX_EXE" list
smoke C07 "status" -- "$WINIX_EXE" status
smoke C08 "bogus command" -- "$WINIX_EXE" bogus-command

# ----- V: --via override -----

smoke V01 "list --via scoop" -- "$WINIX_EXE" list --via scoop
smoke V02 "install --via scoop --dry-run" -- "$WINIX_EXE" install --via scoop --dry-run
smoke V03 "install --via xyz" -- "$WINIX_EXE" install --via xyz
smoke V04 "install --via brew (linux/win → unavailable)" -- "$WINIX_EXE" install --via brew --dry-run
smoke V05 "install --via dotnet --dry-run" -- "$WINIX_EXE" install --via dotnet --dry-run

# ----- N: Network / latency -----

# Latency probe: --version should be fast (no manifest fetch).
echo "[N04 timing] winix --version"
START=$(date +%s%N)
timeout 5s "$WINIX_EXE" --version > /dev/null 2> /dev/null
END=$(date +%s%N)
ELAPSED_MS=$(( (END - START) / 1000000 ))
echo "  --version elapsed: ${ELAPSED_MS}ms" > "$RES/N04.timing.txt"

# Latency probe: list hits the network.
echo "[N05 timing] winix list"
START=$(date +%s%N)
timeout 60s "$WINIX_EXE" list > /dev/null 2> /dev/null
END=$(date +%s%N)
ELAPSED_MS=$(( (END - START) / 1000000 ))
echo "  list elapsed: ${ELAPSED_MS}ms" > "$RES/N05.timing.txt"

# ----- D: Dry-run correctness -----

smoke D01 "install --dry-run (verify no PM spawn)" -- "$WINIX_EXE" install --dry-run
smoke D02 "install --via scoop --dry-run (no EnsureBucket)" -- "$WINIX_EXE" install --via scoop --dry-run
smoke D03 "update --dry-run" -- "$WINIX_EXE" update --dry-run
smoke D04 "uninstall --dry-run" -- "$WINIX_EXE" uninstall --dry-run
smoke D05 "install timeit nonexistent --dry-run" -- "$WINIX_EXE" install timeit nonexistent-zzz --dry-run

# ----- E: Errors -----

smoke E01 "no command" -- "$WINIX_EXE"
smoke E02 "unknown command" -- "$WINIX_EXE" bogus
smoke E03 "invalid --via" -- "$WINIX_EXE" install --via badpm

# E04 (no PM available) and E05 (network failure) require state we don't control;
# document as manual in the report.
echo "[E04 SKIPPED] requires PATH manipulation to remove all PMs; verify manually." > "$RES/E04.note.txt"
echo "[E05 SKIPPED] requires network blocking; verify manually." > "$RES/E05.note.txt"
echo "[E06 SKIPPED] requires synthesizing a malformed manifest URL; verify manually." > "$RES/E06.note.txt"

# Manual section
echo "[M*] All Manual probes deferred — see SUITE-DESIGN.md M section." > "$RES/M_NOTE.txt"

# ----- A: agents subcommand (writes files; isolated temp dirs + WINIX_AGENTS_HOME) -----
#
# MECHANISM (verified 2026-06-09): the smoke/smoke_env helpers do NOT assert exit codes —
# they CAPTURE the command's exit to $RES/$id.exit.txt for inspection. The "-> N" in each
# description is the EXPECTED code, verified by reading the captured artifact (or by the CI
# step that diffs exit.txt against the expectation), not by the harness failing red. The A00
# control below is a known-nonzero command: its $RES/A00.exit.txt MUST be non-zero — if it is
# ever 0, the binary stopped distinguishing bad verbs and every negative-path case is suspect.
# User-scope cases set WINIX_AGENTS_HOME to a scratch dir so the real ~/.claude is never touched.

AGHOME="$RES/agents-home"        # fake user home for user-scope cases (.claude exists)
AGEMPTY="$RES/agents-home-empty" # fake user home with no known agent dirs
AGREPO="$RES/agents-repo"        # fake repo for --project cases
AGREPO2="$RES/agents-repo-claude"
rm -rf "$AGHOME" "$AGEMPTY" "$AGREPO" "$AGREPO2"
mkdir -p "$AGHOME/.claude" "$AGEMPTY" "$AGREPO" "$AGREPO2"

# CONTROL: known-nonzero. A00.exit.txt MUST be non-zero (proves negative-path cases have teeth).
smoke A00 "CONTROL agents bad verb -> nonzero"      -- "$WINIX_EXE" agents frobnicate

# User scope (default) — redirected to the fake home so the real ~/.claude is never touched.
smoke_env A01 "agents no verb -> usage 125"          "WINIX_AGENTS_HOME=$AGHOME" -- "$WINIX_EXE" agents
smoke_env A02 "agents init (user) writes .claude -> 0" "WINIX_AGENTS_HOME=$AGHOME" -- "$WINIX_EXE" agents init
smoke_env A03 "agents status (user) current -> 0"    "WINIX_AGENTS_HOME=$AGHOME" -- "$WINIX_EXE" agents status
smoke_env A04 "agents status --json current -> 0"    "WINIX_AGENTS_HOME=$AGHOME" -- "$WINIX_EXE" agents status --json
smoke_env A05 "agents init idempotent re-run -> 0"   "WINIX_AGENTS_HOME=$AGHOME" -- "$WINIX_EXE" agents init
# Capture the user block NOW — A06 (remove) strips it, so a capture at end-of-run would be empty.
cp "$AGHOME/.claude/CLAUDE.md" "$RES/A.user.CLAUDE.md.txt" 2>/dev/null || true
smoke_env A06 "agents remove (user) -> 0"            "WINIX_AGENTS_HOME=$AGHOME" -- "$WINIX_EXE" agents remove
smoke_env A07 "agents status after remove -> drift 1" "WINIX_AGENTS_HOME=$AGHOME" -- "$WINIX_EXE" agents status

# Empty home (no .claude/.codex, no force) -> usage 125; --codex force-creates -> 0.
smoke_env A08 "agents init no home no force -> 125"  "WINIX_AGENTS_HOME=$AGEMPTY" -- "$WINIX_EXE" agents init
smoke_env A09 "agents init --codex force-creates -> 0" "WINIX_AGENTS_HOME=$AGEMPTY" -- "$WINIX_EXE" agents init --codex

# Project scope (opt-in) — committed-file behaviour, conditional wording.
smoke A10 "agents init --project writes AGENTS.md -> 0" -- "$WINIX_EXE" agents init --project --path "$AGREPO"
smoke A11 "agents status --project current -> 0"        -- "$WINIX_EXE" agents status --project --path "$AGREPO"
smoke A12 "agents init --project --claude both -> 0"    -- "$WINIX_EXE" agents init --project --path "$AGREPO2" --claude
smoke A13 "agents init --project --dry-run -> 0"        -- "$WINIX_EXE" agents init --project --path "$AGREPO" --dry-run

# Validation errors.
smoke A14 "agents --path without --project -> 125" -- "$WINIX_EXE" agents status --path "$AGREPO"
smoke A15 "agents --project with --codex -> 125"   -- "$WINIX_EXE" agents init --project --codex --path "$AGREPO"

# Capture the project block (AGREPO2 is never removed). The user block was captured above,
# before A06 removed it.
cp "$AGREPO2/AGENTS.md" "$RES/A.project.AGENTS.md.txt" 2>/dev/null || true

echo
echo "==== Done. Results in $RES/ ===="
ls "$RES" | wc -l
