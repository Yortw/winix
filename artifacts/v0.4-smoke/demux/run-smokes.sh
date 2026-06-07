#!/bin/bash
set +e
BIN="$(pwd)/artifacts/v0.4-smoke/demux/bin/demux.exe"
OUT="$(pwd)/artifacts/v0.4-smoke/demux/out"
rm -rf "$OUT"; mkdir -p "$OUT"

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
run S04-route "route ERROR to file, rest passthrough" "printf 'ERROR a\ninfo b\n' | $BIN --to ERROR $OUT/err.log"
run S05-exec "exec child receives stdin" "printf 'ERROR x\n' | $BIN --exec ERROR 'cat > $OUT/exec.out'"
run S06-default "default-to file" "printf 'a\n' | $BIN --to ERROR $OUT/e.log --default-to $OUT/rest.log"
run S07-field "field routing" "printf '404\tx\n500\ty\n' | $BIN --field 1 --delimiter '\t' --to '^4' $OUT/c.tsv"
run S08-json "json summary to stderr" "printf 'ERROR a\n' | $BIN --to ERROR $OUT/e2.log --json"
run S09-badregex "bad regex -> 125, readable msg" "printf 'x\n' | $BIN --to '(' $OUT/f.log"
run S10-setupfail "unwritable -> 126, readable msg" "printf 'x\n' | $BIN --to ERROR /no_such_dir_zzz/x.log"
run S11-color "--color forces ANSI" "printf 'ERROR x\n' | $BIN --to ERROR $OUT/e3.log --color"
run S12-nocolor "--no-color" "printf 'ERROR x\n' | $BIN --to ERROR $OUT/e4.log --no-color"

# ── Capability-surface addition (2026-06-07): SIGINT mid-stream ──
# DX1: demux interrupted by SIGINT while routing a slow stream.
# PROBED 2026-06-07 on the linux-x64 binary: demux has NO signal handler (verified
# in source — no CancelKeyPress/PosixSignal), so SIGINT is signal-default termination.
# It dies promptly within timeout's kill window (no escalation), prints NOTHING to
# stderr (no stack trace, no spurious error), and emits no cancel envelope.
# This pins F4(i): a tool with no interrupt envelope asserts ONLY prompt termination
# + ABSENCE of a spurious error/stack line — we do NOT invent an envelope assertion.
# README:193 documents the v1 orphaned-child caveat; this case pins demux's OWN
# exit/output (124 = GNU timeout's own code when it had to send the signal), not
# child cleanup. Linux-only: MSYS/macOS gated out (the Windows harness cannot deliver
# SIGINT to a native exe; macOS runner skips to keep the assertion single-platform).
if [ "$(uname -s)" = "Linux" ]; then
  echo "=== DX1: SIGINT mid-stream -> default termination, no envelope (exit 124 = timeout's own code) ==="
  echo "CMD: slow-producer | timeout -s INT 2 $BIN --to ERROR ..." > "$OUT/DX1.cmd"
  ( for i in $(seq 1 100); do echo "ERROR line $i"; sleep 0.1; done ) \
    | timeout -s INT 2 "$BIN" --to ERROR "$OUT/dx1.log" \
    1>"$OUT/DX1.stdout" 2>"$OUT/DX1.stderr"
  dx1_rc=$?
  echo "$dx1_rc" > "$OUT/DX1.exitcode"
  # Assert: timeout had to send the signal (124), and demux left NO spurious stderr.
  if [ "$dx1_rc" = "124" ] && [ ! -s "$OUT/DX1.stderr" ]; then
    echo "  DX1 PASS: exit=124 (timeout sent INT), stderr empty (no stack/spurious error)"
  else
    echo "  DX1 FAIL: exit=$dx1_rc, stderr bytes=$(wc -c < "$OUT/DX1.stderr")"
    echo "  --- DX1 stderr ---"; cat "$OUT/DX1.stderr"
  fi
else
  echo "=== DX1: SKIPPED ($(uname -s): SIGINT-to-native-exe smoke is Linux-only) ==="
  echo "skipped" > "$OUT/DX1.exitcode"
fi

echo "=== Smoke run complete ==="
