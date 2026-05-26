#!/bin/bash
# Tier-1 smoke suite for retry — baseline-12 + retry-specific extensions.
set +e
BIN="d:/projects/winix/artifacts/round-stop-2026-05-09/retry/fresh-publish/retry.exe"
OUT="d:/projects/winix/artifacts/round-stop-2026-05-09/retry/out"
mkdir -p "$OUT"

run() {
  local id="$1"; local desc="$2"; shift 2
  echo "=== $id: $desc ==="
  echo "$@" > "$OUT/$id.cmd"
  timeout 30s "$@" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo "$?" > "$OUT/$id.exit"
}

run_env() {
  local id="$1"; local desc="$2"; local env="$3"; shift 3
  echo "=== $id: $desc (env: $env) ==="
  echo "env $env $@" > "$OUT/$id.cmd"
  timeout 30s env $env "$@" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo "$?" > "$OUT/$id.exit"
}

# ── Baseline 12 ──
run B01 "--version" "$BIN" --version
run B02 "--help" "$BIN" --help
run B03 "--describe" "$BIN" --describe
run B04 "no args" "$BIN"
run B05 "unknown flag" "$BIN" --invalid-flag
run B06 "happy path: trivial command" "$BIN" -- bash -c "exit 0"
run B07 "command exit pass-through (exit 7)" "$BIN" -- bash -c "exit 7"
run_env B08 "NO_COLOR + happy" "NO_COLOR=1" "$BIN" -- bash -c "exit 0"
run B09 "--no-color flag" "$BIN" --no-color -- bash -c "exit 0"
run B10 "--color forced" "$BIN" --color -- bash -c "exit 0"
run B11 "missing -- separator" "$BIN" exit 0
run B12 "command not found" "$BIN" -- this-command-does-not-exist-xyz-12345

# ── Retry-specific extensions (correct flag names: -n / --times, -d / --delay) ──
run R01 "--times 2 --on 7 with exit-7 cmd (retries then fails)" "$BIN" --times 2 --on 7 --delay 100ms -- bash -c "exit 7"
run R02 "--times 0 with exit-7 cmd (no retries)" "$BIN" --times 0 -- bash -c "exit 7"
run R03 "--times 3 success first try" "$BIN" --times 3 -- bash -c "exit 0"
run R04 "--json output success" "$BIN" --json --times 1 -- bash -c "exit 0"
run R05 "bad --times value" "$BIN" --times not-a-number -- bash -c "exit 0"
run R06 "bad --on value" "$BIN" --on bogus -- bash -c "exit 1"
run R07 "--backoff exp small delay" "$BIN" --times 2 --backoff exp --delay 50ms -- bash -c "exit 1"

echo
echo "==== retry done: $(ls $OUT | wc -l) result files ===="
