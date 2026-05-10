#!/bin/bash
# Tier-1 smoke suite for peep (file watcher / live-refresh).
set +e
BIN="d:/projects/winix/artifacts/round-stop-2026-05-09/peep/fresh-publish/peep.exe"
OUT="d:/projects/winix/artifacts/round-stop-2026-05-09/peep/out"
DATA="d:/projects/winix/artifacts/round-stop-2026-05-09/peep/data"
mkdir -p "$OUT" "$DATA"

# Test fixture: a known small file
echo "fixture content" > "$DATA/fixture.txt"

run() {
  local id="$1"; local desc="$2"; shift 2
  echo "=== $id: $desc ==="
  echo "$@" > "$OUT/$id.cmd"
  timeout 5s "$@" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo "$?" > "$OUT/$id.exit"
}

# ── Baseline 12 ──
run B01 "--version" "$BIN" --version
run B02 "--help" "$BIN" --help
run B03 "--describe" "$BIN" --describe
run B04 "no args" "$BIN"
run B05 "unknown flag" "$BIN" --invalid-flag
run B06 "--once happy: bash exit 0" "$BIN" --once -- bash -c "echo hello"
run B07 "--once with command failure" "$BIN" --once -- bash -c "exit 7"
run B08 "--once command not found" "$BIN" --once -- this-cmd-not-real-xyz
run B09 "watch mode bare (timeout-bound)" "$BIN" -- bash -c "echo tick"
run B10 "--no-color" "$BIN" --no-color --once -- bash -c "echo a"
run B11 "missing -- separator" "$BIN" --once
run B12 "NO_COLOR env" env NO_COLOR=1 "$BIN" --once -- bash -c "echo a"

# ── Peep-specific extensions ──
run P01 "--once with --json" "$BIN" --once --json -- bash -c "echo a"
run P02 "--interval invalid" "$BIN" --interval not-a-duration -- bash -c "echo a"
run P03 "--exit-on-change one-shot" "$BIN" --once --exit-on-change -- bash -c "echo a"

echo
echo "==== peep done: $(ls $OUT | wc -l) result files ===="
