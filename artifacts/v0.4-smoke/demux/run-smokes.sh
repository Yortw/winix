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

echo "=== Smoke run complete ==="
