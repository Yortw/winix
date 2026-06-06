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

# ── Capability-surface additions (2026-06-06) ──
# R08: SIGINT cancellation mid-child — retry's most safety-critical path (Ctrl+C ->
# kill child tree -> grace window -> pass-through exit).
# EXPECTED RESULT: exit file = 124 AND stderr envelope contains "exit_reason":"cancelled".
# The 124 is GNU timeout's OWN semantics (it reports 124 whenever it had to send the
# signal, regardless of the child's subsequent code). Probed 2026-06-06: retry exits 130
# ~50ms after the INT (timeout signals its process GROUP, so the sleep child also gets
# INT directly -> child_exit_code 130), envelope says cancelled, nothing lingers. A wedged
# cancel path shows up as a missing envelope and ~30s wall (outer run() timeout) instead
# of ~2s. Linux-only: MSYS cannot deliver SIGINT to a native Windows exe; on Windows this
# path is covered by manual real-terminal Ctrl+C smoke.
if [ "$(uname -s)" = "Linux" ]; then
  run R08 "SIGINT mid-child -> cancelled envelope (exit 124 = timeout's own code)" timeout -s INT 2 "$BIN" --json -- sleep 20
else
  echo "=== R08: SKIPPED (Windows: no SIGINT delivery to native exe from this harness) ==="
  echo "skipped" > "$OUT/R08.exit"
fi

echo
echo "==== retry done: $(ls $OUT | wc -l) result files ===="
