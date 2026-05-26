#!/bin/bash
# Tier-1 smoke suite for wargs (xargs equivalent).
set +e
BIN="d:/projects/winix/artifacts/round-stop-2026-05-09/wargs/fresh-publish/wargs.exe"
OUT="d:/projects/winix/artifacts/round-stop-2026-05-09/wargs/out"
mkdir -p "$OUT"

run() {
  local id="$1"; local desc="$2"; shift 2
  echo "=== $id: $desc ==="
  echo "$@" > "$OUT/$id.cmd"
  timeout 30s "$@" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo "$?" > "$OUT/$id.exit"
}

run_stdin() {
  local id="$1"; local desc="$2"; local stdin_text="$3"; shift 3
  echo "=== $id: $desc (stdin: $stdin_text) ==="
  echo "$@" > "$OUT/$id.cmd"
  echo -n "$stdin_text" | timeout 30s "$@" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo "$?" > "$OUT/$id.exit"
}

# ── Baseline 12 ──
run B01 "--version" "$BIN" --version
run B02 "--help" "$BIN" --help
run B03 "--describe" "$BIN" --describe
run B04 "no args" "$BIN"
run B05 "unknown flag" "$BIN" --invalid-flag
run B06 "happy path: bash exit 0" "$BIN" -- bash -c "exit 0"
run_stdin B07 "stdin items pass through" "a b c" "$BIN" -- echo
run B08 "command not found" "$BIN" -- this-cmd-does-not-exist-xyz-12345
run_stdin B09 "ndjson output" "x y" "$BIN" --ndjson -- echo
run B10 "missing -- separator (no command)" "$BIN" arg
run_stdin B11 "empty stdin" "" "$BIN" -- echo
run B12 "--no-color" "$BIN" --no-color -- bash -c "exit 0"

# ── Wargs-specific extensions ──
run_stdin W01 "--keep-order with multi items" "1 2 3" "$BIN" --keep-order -- echo
run_stdin W02 "--parallel 2" "a b c d" "$BIN" --parallel 2 -- echo
run_stdin W03 "exit code aggregation: one fails" "0 1" "$BIN" -- bash -c 'exit "$0"'
run W04 "--describe shape sanity" "$BIN" --describe

echo
echo "==== wargs done: $(ls $OUT | wc -l) result files ===="
