#!/bin/bash
set +e
BIN="$(pwd)/artifacts/v0.4-smoke/runfor/bin/runfor.exe"
OUT="$(pwd)/artifacts/v0.4-smoke/runfor/out"
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

# S04: command exits 0 well within the deadline (expect exit 0).
run S04-success "child exits 0 before deadline (expect 0)" "$BIN 30s -- bash -c 'exit 0'"

# S05: deadline fires before the child finishes (expect exit 124).
# --kill-after 200ms ensures SIGKILL backstop fires after the SIGTERM window,
# making termination deterministic on every platform (Windows kills immediately;
# Unix SIGKILL tree reap is guaranteed). Child is a 30s sleep so it always
# outlasts the 200ms deadline.
run S05-timeout "deadline fires -> child killed (expect 124)" "$BIN --kill-after 200ms 200ms -- sleep 30"

# S06: --json envelope on a timeout (expect exit 124, stderr contains "timed_out":true).
# --kill-after keeps it deterministic on Unix; Windows ignores it but still kills promptly.
run S06-json-timeout "--json envelope on timeout (expect 124, stderr timed_out:true)" "$BIN --kill-after 200ms --json 200ms -- sleep 30"

echo "=== S06 timed_out assertion ==="
if grep -q '"timed_out":true' "$OUT/S06-json-timeout.stderr"; then
  echo "  S06 PASS: stderr contains timed_out:true"
else
  echo "  S06 FAIL: expected timed_out:true in stderr"
  echo "  --- S06 stderr ---"; cat "$OUT/S06-json-timeout.stderr"
fi

# S07: bad DURATION argument -> usage error (expect exit 125).
run S07-bad-duration "bad duration arg (expect 125)" "$BIN notaduration -- bash -c 'exit 0'"

# S08: command not found on PATH (expect exit 127).
run S08-not-found "command not found (expect 127)" "$BIN 5s -- this-command-does-not-exist-xyzzy"

run S09-nocolor "--no-color" "$BIN --no-color 30s -- bash -c 'exit 0'"
run S10-color "--color" "$BIN --color 30s -- bash -c 'exit 0'"

echo "=== Smoke run complete ==="
