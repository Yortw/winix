#!/bin/bash
set +e
BIN="$(pwd)/artifacts/reverify-2026-05-06/ids/bin/ids.exe"
OUT="$(pwd)/artifacts/reverify-2026-05-06/ids/out"
mkdir -p "$OUT"

run() {
  local id="$1"; local desc="$2"; shift 2
  local cmd="$*"
  echo "=== $id: $desc ==="
  echo "CMD: $cmd" > "$OUT/$id.cmd"
  eval "$cmd" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo $? > "$OUT/$id.exitcode"
  echo "  exit=$(cat "$OUT/$id.exitcode")  stdout=$(wc -c < "$OUT/$id.stdout")B  stderr=$(wc -c < "$OUT/$id.stderr")B"
}

# Baseline 12
run S01-help "--help" "$BIN --help"
run S02-version "--version" "$BIN --version"
run S03-describe "--describe valid JSON" "$BIN --describe"
run S04-happypath "default uuid7" "$BIN"
run S05-empty "no args - default still works" "$BIN"
run S06a-exit0 "exit 0 happy" "$BIN -t uuid4"
run S06b-exit125-bad-type "exit 125 unknown type" "$BIN -t bogus"
run S06c-exit125-bad-flag "exit 125 unknown flag" "$BIN --not-a-flag"
run S06d-exit125-bad-count "exit 125 negative count" "$BIN -n -5"
run S07-json "--json shape" "$BIN --json -t uuid4"
run S08a-stdout "stdout-only routing" "$BIN -t uuid4 2>/dev/null"
run S08b-stderr "stderr on error" "$BIN -t bogus 1>/dev/null"
run S09a-no-color "--no-color" "$BIN --no-color -t uuid4"
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN -t uuid4"
run S09c-color "--color force" "$BIN --color -t uuid4"
run S10-pipe "stdin not consumed (no input)" "echo blah | $BIN"
run S11-missing-arg "missing required value" "$BIN -t"
run S12-pathspace "path with space — N/A for ids" "$BIN -t uuid4"

# ids-specific category
run S13-large-count "10000 IDs" "$BIN -n 10000"
run S14a-uuid4 "uuid4 type" "$BIN -t uuid4 -n 5"
run S14b-uuid7 "uuid7 type" "$BIN -t uuid7 -n 5"
run S14c-ulid "ulid type" "$BIN -t ulid -n 5"
run S14d-nanoid "nanoid type" "$BIN -t nanoid -n 5"
run S15-uuid7-monotonic "uuid7 monotonic ordering (sort -c)" "$BIN -t uuid7 -n 100"
run S16-ulid-monotonic "ulid monotonic ordering" "$BIN -t ulid -n 100"
run S17-nanoid-length "nanoid -l 10" "$BIN -t nanoid -l 10 -n 3"
run S18-nanoid-alphabet-hex "nanoid --alphabet hex" "$BIN -t nanoid --alphabet hex -l 16"
run S19-nanoid-alphabet-bogus "nanoid --alphabet bogus → 125" "$BIN -t nanoid --alphabet bogus"
run S20-uppercase-uuid "-u uppercase uuid" "$BIN -u -t uuid4"
run S21-format-braces "--format braces" "$BIN -t uuid4 --format braces"
run S22-format-urn "--format urn" "$BIN -t uuid4 --format urn"
run S23-format-hex "--format hex" "$BIN -t uuid4 --format hex"
run S24-flag-mismatch "--length on uuid4 → 125" "$BIN -t uuid4 --length 10"
run S25-zero-count "-n 0" "$BIN -n 0"

echo "=== Smoke run complete ==="
