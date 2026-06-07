#!/bin/bash
set +e
BIN="$(pwd)/artifacts/v0.4-smoke/hcat/bin/hcat.exe"
OUT="$(pwd)/artifacts/v0.4-smoke/hcat/out"
rm -rf "$OUT"; mkdir -p "$OUT"

# F8 safety: hcat is a server. Every server case uses --capture 1 (a CI stop
# condition: hcat exits 0 after one completed request) plus --timeout so no
# listener can ever be left bound after the fixture exits. We fire a single
# curl in the background to satisfy the capture, then wait for hcat to exit.

WORK="$OUT/work"
mkdir -p "$WORK"
printf 'hello from hcat\n' > "$WORK/index.html"

run() {
  local id="$1"; local desc="$2"; shift 2
  local cmd="$*"
  echo "=== $id: $desc ==="
  echo "CMD: $cmd" > "$OUT/$id.cmd"
  eval "$cmd" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo $? > "$OUT/$id.exitcode"
  echo "  exit=$(cat "$OUT/$id.exitcode")  stdout=$(wc -c < "$OUT/$id.stdout")B  stderr=$(wc -c < "$OUT/$id.stderr")B"
}

# Pick a high, likely-free port per case to avoid TIME_WAIT collisions.
PORT=8731

# Run a server case bounded by --capture 1 + --timeout, satisfied by one curl.
# Args: id, desc, then the hcat args (mode + flags). --capture/--timeout/--port
# are appended here. Guarantees no orphaned listener: hcat exits when the single
# request completes, or when --timeout fires (exit 1).
run_server() {
  local id="$1"; local desc="$2"; shift 2
  PORT=$((PORT + 1))
  local url="http://127.0.0.1:$PORT/"
  echo "=== $id: $desc (port $PORT) ==="
  echo "CMD: $BIN $* --capture 1 --timeout 15s --port $PORT" > "$OUT/$id.cmd"
  "$BIN" "$@" --capture 1 --timeout 15s --port "$PORT" \
    1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr" &
  local hpid=$!
  # Give Kestrel a moment to bind, then deliver exactly one request.
  sleep 2
  curl -s -m 5 -X POST --data-binary 'deploy-complete' "$url" \
    1>"$OUT/$id.client.out" 2>/dev/null
  # Wait for hcat to exit on the capture condition (bounded by its own --timeout).
  wait "$hpid"
  echo $? > "$OUT/$id.exitcode"
  echo "  exit=$(cat "$OUT/$id.exitcode")  stdout=$(wc -c < "$OUT/$id.stdout")B  stderr=$(wc -c < "$OUT/$id.stderr")B"
}

run S01-help "--help" "$BIN --help"
run S02-version "--version" "$BIN --version"
run S03-describe "--describe" "$BIN --describe"

run_server S04-serve "serve a dir, one request then stop" serve "$WORK"
run_server S05-inspect "inspect one request then stop" inspect
run_server S06-inspect-json "inspect --json (JSONL to stdout)" inspect --json
# S07 pipe is bespoke: hcat's own flags MUST precede the `pipe -- <cmd>` child
# separator, otherwise everything after `--` (incl. --capture/--timeout/--port) is
# swallowed into the child argv — hcat then binds the DEFAULT port with no stop
# condition and runs unbounded (orphaned listener). run_server appends flags, which
# is incompatible with `--`, so this case is inlined with flags placed correctly.
PORT=$((PORT + 1))
echo "=== S07-pipe: pipe --capture/timeout/port BEFORE -- tr a-z A-Z (port $PORT) ==="
echo "CMD: $BIN pipe --capture 1 --timeout 15s --port $PORT -- tr a-z A-Z" > "$OUT/S07-pipe.cmd"
"$BIN" pipe --capture 1 --timeout 15s --port "$PORT" -- tr a-z A-Z \
  1>"$OUT/S07-pipe.stdout" 2>"$OUT/S07-pipe.stderr" &
s07pid=$!
sleep 2
curl -s -m 5 -X POST --data-binary 'deploy-complete' "http://127.0.0.1:$PORT/" \
  1>"$OUT/S07-pipe.client.out" 2>/dev/null
wait "$s07pid"
echo $? > "$OUT/S07-pipe.exitcode"
echo "  exit=$(cat "$OUT/S07-pipe.exitcode")"

run_server S08-serve-json "serve --json access-log" serve "$WORK" --json
run_server S09-color "serve --color" serve "$WORK" --color

# Bad bind -> startup failure 126. Use an unassignable address (TEST-NET-3, RFC 5737)
# rather than a privileged port: AddressNotAvailable fails identically on Windows, Linux,
# and macOS, whereas "--port 1" is bindable for a local user on Windows.
run S10-badbind "unbindable host -> 126" "$BIN serve $WORK --host 203.0.113.1 --port 8799 --capture 1 --timeout 5s"

# ── Capability-surface addition (2026-06-07): SIGINT during serve ──
# HX1: hcat serve interrupted by SIGINT.
# PROBED 2026-06-07 on the linux-x64 binary. hcat wires Console.CancelKeyPress for a
# graceful shutdown (Cli.cs). Two distinct behaviours, both verified:
#   * INTERACTIVE Ctrl+C (real PTY, signal to the foreground process group): hcat
#     runs the graceful shutdown and exits 0 — README:203's "clean shutdown (Ctrl+C)"
#     claim is TRUE and was confirmed by a PTY probe (COMMAND_EXIT_CODE="0").
#   * NON-INTERACTIVE / CI (no controlling terminal, SIGINT via `timeout -s INT` to a
#     backgrounded process): hcat does NOT self-terminate within a short window — the
#     Kestrel host's lifetime shutdown does not complete without a TTY session, so
#     GNU timeout escalates to SIGKILL after --kill-after (exit 137).
# A CI smoke runs the non-interactive path, so this case pins exit 137 (timeout had to
# escalate). This is NOT a doc bug: README:203 describes interactive Ctrl+C, which the
# PTY probe verifies. The load-bearing assertion is the tool's behaviour under the
# signal — that it is forcibly stopped and leaves no spurious error line. Any change to
# make hcat exit promptly under a non-TTY SIGINT is OUT of scope (follow-up note only).
# Linux-only: MSYS/macOS gated out (Windows harness cannot deliver SIGINT to a native
# exe; macOS runner skips to keep the assertion single-platform).
if [ "$(uname -s)" = "Linux" ]; then
  HXPORT=8799
  echo "=== HX1: SIGINT during serve -> non-TTY path, timeout escalates to KILL (exit 137) ==="
  echo "CMD: timeout -s INT --kill-after 4 2 $BIN serve $OUT --port $HXPORT" > "$OUT/HX1.cmd"
  timeout -s INT --kill-after 4 2 "$BIN" serve "$OUT" --port "$HXPORT" \
    1>"$OUT/HX1.stdout" 2>"$OUT/HX1.stderr"
  hx1_rc=$?
  echo "$hx1_rc" > "$OUT/HX1.exitcode"
  # Assert: hcat was forcibly terminated (137 = timeout's SIGKILL escalation) and left
  # no spurious error/stack line (the banner may or may not flush before KILL — only a
  # stack trace / "error:" line would be a defect, so check for those specifically).
  if [ "$hx1_rc" = "137" ] && ! grep -qiE "error:|exception|stack trace|at [A-Za-z].*\(" "$OUT/HX1.stderr"; then
    echo "  HX1 PASS: exit=137 (timeout escalated to SIGKILL), no spurious error/stack in stderr"
  else
    echo "  HX1 FAIL: exit=$hx1_rc (expected 137)"
    echo "  --- HX1 stderr ---"; cat "$OUT/HX1.stderr"
  fi
else
  echo "=== HX1: SKIPPED ($(uname -s): SIGINT-to-native-exe smoke is Linux-only) ==="
  echo "skipped" > "$OUT/HX1.exitcode"
fi

# Teardown: nothing to kill — all servers were --capture/--timeout bounded, and HX1's
# server was terminated by timeout's SIGKILL escalation.
rm -rf "$WORK"

echo "=== Smoke run complete ==="
