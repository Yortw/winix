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

echo
echo "==== Done. Results in $RES/ ===="
ls "$RES" | wc -l
