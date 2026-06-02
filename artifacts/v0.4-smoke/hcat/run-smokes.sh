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

# Teardown: nothing to kill — all servers were --capture/--timeout bounded.
rm -rf "$WORK"

echo "=== Smoke run complete ==="
