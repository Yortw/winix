#!/bin/bash
set +e
BIN="$(pwd)/artifacts/v0.4-smoke/mksecret/bin/mksecret.exe"
OUT="$(pwd)/artifacts/v0.4-smoke/mksecret/out"
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
run S04-default "default password generation" "$BIN"
run S05-length "password --length 32" "$BIN --length 32"
run S06-charset-full "password --charset full" "$BIN --charset full"
run S07-charset-safe "password --charset safe" "$BIN --charset safe"
run S08-count "password --count 5" "$BIN --count 5"
run S09-phrase "phrase subcommand" "$BIN phrase"
run S10-key "key subcommand --encoding hex" "$BIN key --encoding hex"
run S11-json "password --json to stdout" "$BIN --count 3 --json"
run S12-nocolor "--no-color" "$BIN --no-color"
run S13-color "--color" "$BIN --color"
run S14-quiet "--quiet suppresses entropy note" "$BIN --quiet"
run S15-badcharset "invalid charset -> 125" "$BIN --charset nope"
run S16-badarg "unknown flag -> 125" "$BIN --bogus-flag"

echo "=== Smoke run complete ==="
