#!/bin/bash
set +e
BIN="$(pwd)/artifacts/v0.4-smoke/trash/bin/trash.exe"
OUT="$(pwd)/artifacts/v0.4-smoke/trash/out"
rm -rf "$OUT"; mkdir -p "$OUT"

# F8 idempotency/safety: all throwaway files live INSIDE WORK and are the only
# things ever trashed — never a real path. Teardown removes WORK at the end.
WORK="$OUT/work"
mkdir -p "$WORK"

run() {
  local id="$1"; local desc="$2"; shift 2
  local cmd="$*"
  echo "=== $id: $desc ==="
  echo "CMD: $cmd" > "$OUT/$id.cmd"
  eval "$cmd" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo $? > "$OUT/$id.exitcode"
  echo "  exit=$(cat "$OUT/$id.exitcode")  stdout=$(wc -c < "$OUT/$id.stdout")B  stderr=$(wc -c < "$OUT/$id.stderr")B"
}

run S01-help "--help" "$BIN --help"
run S02-version "--version" "$BIN --version"
run S03-describe "--describe" "$BIN --describe"
run S04-list-empty "--list (may be empty)" "$BIN --list"

# Make a throwaway file inside WORK and trash THAT (never a real path).
printf 'disposable\n' > "$WORK/throwaway1.txt"
run S05-trash "trash a throwaway file" "$BIN $WORK/throwaway1.txt"

printf 'disposable2\n' > "$WORK/throwaway2.txt"
run S06-trash-json "trash a throwaway file --json" "$BIN $WORK/throwaway2.txt --json"

run S07-list-after "--list after trashing" "$BIN --list"
run S08-list-json "--list --json" "$BIN --list --json"
run S09-missing "nonexistent target -> error" "$BIN $WORK/does_not_exist_zzz.txt"
run S10-noargs "no paths -> 125 usage" "$BIN"
run S11-color "--list --color" "$BIN --list --color"
run S12-nocolor "--list --no-color" "$BIN --list --no-color"

# ─── W: Windows glob expansion (Windows-only; skipped on Linux/macOS) ───
IS_WINDOWS=false
case "$(uname -s)" in MINGW*|CYGWIN*|MSYS*) IS_WINDOWS=true ;; esac

if $IS_WINDOWS; then
  GLOB_WORK="$OUT/work-glob"
  rm -rf "$GLOB_WORK"
  mkdir -p "$GLOB_WORK"
  printf 'x\n' > "$GLOB_WORK/x.log"
  printf 'y\n' > "$GLOB_WORK/y.log"
  printf 'keep\n' > "$GLOB_WORK/keep.txt"

  # W01: *.log expands — trash moves x.log + y.log, keep.txt stays, exit 0
  # Subshell cd prevents MSYS-style absolute path (/d/...) from reaching the tool; the tool
  # sees only the relative bare pattern *.log and expands it against its cwd.
  run W01-glob-expand "glob *.log expands to two files (exit 0)" "( cd '$GLOB_WORK' && '$BIN' '*.log' )"

  # W02: ** → usage error, exit 125
  # Subshell-relative: avoids MSYS-path passthrough; tool sees literal ** from cwd.
  run W02-double-star "** rejected with usage error (exit 125)" "( cd '$GLOB_WORK' && '$BIN' '**' )"

  # W03: no-match pattern → literal passthrough → not-found, exit 1
  # Subshell-relative: avoids MSYS-path passthrough; tool sees literal *.nope from cwd.
  run W03-no-match "*.nope no-match → not-found (exit 1)" "( cd '$GLOB_WORK' && '$BIN' '*.nope' )"

  rm -rf "$GLOB_WORK"
else
  echo "=== W: Windows glob expansion — SKIPPED (not Windows) ==="
fi

# Teardown: remove the WORK directory (the trashed copies live in the OS trash;
# this just cleans the staging dir).
rm -rf "$WORK"

echo "=== Smoke run complete ==="
