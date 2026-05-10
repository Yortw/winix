#!/bin/bash
# Category-aware smoke suite for whoholds.
# Run from repo root: bash artifacts/reverify-2026-05-06/whoholds/run-smokes.sh
# WARNING: spawns a PowerShell process to hold a file lock + a Python http.server
# for port-query test. Both are killed at the end (best-effort).

set +e
BIN="$(pwd)/artifacts/reverify-2026-05-06/whoholds/bin/whoholds.exe"
OUT="$(pwd)/artifacts/reverify-2026-05-06/whoholds/out"
DATA="$(pwd)/artifacts/reverify-2026-05-06/whoholds/data"
mkdir -p "$OUT" "$DATA"
mkdir -p "$DATA/dir with space"

# Test data
echo "test content" > "$DATA/test.txt"
echo "spaced content" > "$DATA/dir with space/spaced.txt"
echo "8080" > "$DATA/8080"          # for C13c — bare-number arg that IS a file
LONG_NAME=$(printf 'X%.0s' {1..240}).txt
echo "long" > "$DATA/$LONG_NAME"

run() {
  local id="$1"; local desc="$2"; shift 2
  local cmd="$*"
  echo "=== $id: $desc ==="
  echo "CMD: $cmd" > "$OUT/$id.cmd"
  eval "$cmd" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo $? > "$OUT/$id.exitcode"
  echo "  exit=$(cat "$OUT/$id.exitcode")  stdout=$(wc -c < "$OUT/$id.stdout")B  stderr=$(wc -c < "$OUT/$id.stderr")B"
}

# Pick a random high port for the bound-port test
RANDOM_PORT=$((30000 + RANDOM % 5000))
UNBOUND_PORT=$((40000 + RANDOM % 5000))

# Spawn a Python http.server in the background for the bound-port test
echo "[setup] starting Python http.server on port $RANDOM_PORT"
python -m http.server "$RANDOM_PORT" --bind 127.0.0.1 >/dev/null 2>&1 &
PY_PID=$!
sleep 1.5  # give it time to bind

# Spawn a PowerShell process holding test.txt with FileShare.None
echo "[setup] starting PowerShell file-lock holder"
LOCKED_FILE="$DATA/locked.txt"
echo "locked content" > "$LOCKED_FILE"
# Use /D to convert path; powershell needs Windows-style path
LOCKED_WIN=$(cygpath -w "$LOCKED_FILE" 2>/dev/null || echo "$LOCKED_FILE")
powershell.exe -NoProfile -Command "\$h = [System.IO.File]::Open('$LOCKED_WIN', 'Open', 'Read', 'None'); Start-Sleep -Seconds 60; \$h.Close()" >/dev/null 2>&1 &
PS_PID=$!
sleep 1.5  # give it time to acquire the lock

cleanup() {
  echo "[cleanup] killing helpers ($PY_PID, $PS_PID)"
  kill "$PY_PID" 2>/dev/null
  kill "$PS_PID" 2>/dev/null
  # Best-effort kill via taskkill in case bash kill doesn't reach the Windows process
  taskkill //F //PID "$PY_PID" >/dev/null 2>&1
  taskkill //F //PID "$PS_PID" >/dev/null 2>&1
}
trap cleanup EXIT

# ─── Baseline 12 ───
run S01-help "--help" "$BIN --help"
run S02-version "--version" "$BIN --version"
run S03-describe "--describe valid JSON" "$BIN --describe"
run S04-happypath "query existing file with no holders" "$BIN \"$DATA/test.txt\""
run S05-no-positional "no positional arg → 125" "$BIN"
run S06a-exit0-no-holders "exit 0 (no holders)" "$BIN \"$DATA/test.txt\""
run S06b-file-not-found "non-existent file → behaviour TBD" "$BIN \"$DATA/does-not-exist.bin\""
run S06c-exit125 "bad flag → 125" "$BIN --bogus-flag \"$DATA/test.txt\""
run S07-json "--json shape (lands on stderr per docs)" "$BIN --json \"$DATA/test.txt\""
run S08a-stdout-only "default no-holders → stdout empty" "$BIN \"$DATA/test.txt\" 2>/dev/null"
run S08b-stderr-only "no-holders message + json → stderr" "$BIN --json \"$DATA/test.txt\" 1>/dev/null"
run S09a-no-color "--no-color" "$BIN --no-color \"$DATA/test.txt\""
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN \"$DATA/test.txt\""
run S09c-color-force "--color force" "$BIN --color \"$DATA/test.txt\""
run S10-pipe-auto-pidonly "piped output → auto --pid-only" "$BIN \"$DATA/test.txt\" | wc -l"
run S11-bad-flag "bad flag → 125" "$BIN --not-a-flag"
run S12-pathspace "path with space" "$BIN \"$DATA/dir with space/spaced.txt\""

# ─── C13: argument disambiguation ───
run C13a-colon-port ":8080 → port query" "$BIN :8080"
run C13b-bare-number-no-file "8080 (no file '8080' → port)" "$BIN 8080"
run C13c-bare-number-is-file "8080 (file 8080 exists → file query)" "cd \"$DATA\" && $BIN 8080"
run C13d-path-sep "path/with/sep → file even if missing" "$BIN \"some/missing/path.bin\""
run C13e-colon-non-numeric ":abc → 125" "$BIN :abc"
run C13f-colon-zero ":0 port edge" "$BIN :0"
run C13g-colon-max-port ":65535 max valid port" "$BIN :65535"
run C13h-colon-out-of-range ":65536 out of range → 125" "$BIN :65536"
run C13i-colon-negative ":-1 negative → 125" "$BIN :-1"

# ─── C14: file query ───
run C14a-no-holders "existing file, no holders" "$BIN \"$DATA/test.txt\""
run C14b-not-found "missing file (TBD: exit 1?)" "$BIN \"$DATA/does-not-exist.bin\""
run C14c-locked-file "actually-locked file (PowerShell holding) → at least one holder" "$BIN \"$DATA/locked.txt\""
run C14d-pathspace "path with space" "$BIN \"$DATA/dir with space/spaced.txt\""
run C14e-long-path "240-char filename" "$BIN \"$DATA/$LONG_NAME\""
run C14f-relative-path "relative path" "cd \"$DATA\" && $BIN test.txt"
run C14g-trailing-sep "trailing-sep dir/" "$BIN \"$DATA/\""

# ─── C15: port query ───
run C15a-bound-port "Python http.server on $RANDOM_PORT (should detect)" "$BIN \":$RANDOM_PORT\""
run C15b-unbound-port "port $UNBOUND_PORT (no listener, no holders)" "$BIN \":$UNBOUND_PORT\""

# ─── C16: output formats ───
run C16a-pid-only "--pid-only on locked file" "$BIN --pid-only \"$DATA/locked.txt\""
run C16b-full-path "--full-path on locked file" "$BIN --full-path \"$DATA/locked.txt\""
run C16c-json-locked "--json on locked file (full shape on stderr)" "$BIN --json \"$DATA/locked.txt\""
run C16d-json-pidonly "--json + --pid-only interaction" "$BIN --json --pid-only \"$DATA/locked.txt\""

# ─── C17: elevation warning ───
run C17a-elev-warning "elevation warning emitted on stderr (unelevated run)" "$BIN \"$DATA/test.txt\""

# ─── C18: --describe parity ───
run C18a-describe-exitcodes "describe exit codes match {0, 1, 125}" "$BIN --describe"

# ─── C19: documented examples ───
run C19a-readme-example1 "README ex1: whoholds <file>" "$BIN \"$DATA/test.txt\""
run C19b-readme-example2 "README ex2: whoholds :8080" "$BIN :8080"
run C19c-readme-example3 "README ex3: --pid-only piped" "$BIN \"$DATA/test.txt\" --pid-only"

# ─── C20: pipe composition ───
run C20a-pipe-wc "whoholds locked.txt --pid-only | wc -l" "$BIN \"$DATA/locked.txt\" --pid-only | wc -l"

echo
echo "=== Smoke run complete. Outputs in: $OUT ==="
ls "$OUT" | wc -l
echo "(4 files per smoke: cmd, stdout, stderr, exitcode)"
