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

# Teardown: remove the WORK directory (the trashed copies live in the OS trash;
# this just cleans the staging dir).
rm -rf "$WORK"

echo "=== Smoke run complete ==="
