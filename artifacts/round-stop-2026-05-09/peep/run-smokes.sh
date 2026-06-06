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

# ── Capability-surface additions (2026-06-06) ──
# P04: --json-output implies a JSON error envelope on early-return paths even without --json
# (R5 SFH I1 bridging). Probed on the AOT binary 2026-06-06:
#   exit 125, stderr = {"tool":"peep",...,"exit_code":125,"exit_reason":"usage_error"}
run P04 "--json-output bridging: no command -> JSON envelope" "$BIN" --json-output

# P05: once-mode SIGINT cancellation (the OperationCanceledException arm; kill-on-cancel
# must terminate the child promptly).
# EXPECTED RESULT: exit file = 124 AND stderr envelope = {"...,"exit_code":130,"exit_reason":"cancelled"}.
# The 124 is GNU timeout's OWN semantics — when it reaches the limit and sends the signal,
# it reports 124 regardless of the child's subsequent exit code (debugged 2026-06-06:
# peep exits 130 ~340ms after the INT; nothing lingers). The envelope is therefore the
# load-bearing assertion: "cancelled" proves the OCE arm ran; a WEDGED cancel path shows
# up as a missing/absent envelope and a ~5s wall time instead of ~2.3s.
# Linux-only: MSYS cannot deliver SIGINT to a native Windows exe (translates to a hard
# terminate, never reaching CancelKeyPress), so on Windows this path is covered by the
# manual real-terminal Ctrl+C smoke instead.
if [ "$(uname -s)" = "Linux" ]; then
  run P05 "once-mode SIGINT cancel -> cancelled envelope (exit 124 = timeout's own code)" timeout -s INT 2 "$BIN" --once --json -- sleep 20
else
  echo "=== P05: SKIPPED (Windows: no SIGINT delivery to native exe from this harness) ==="
  echo "skipped" > "$OUT/P05.exit"
fi

echo
echo "==== peep done: $(ls $OUT | wc -l) result files ===="
