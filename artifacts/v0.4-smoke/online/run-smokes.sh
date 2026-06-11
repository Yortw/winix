#!/bin/bash
set +e
BIN="$(pwd)/artifacts/v0.4-smoke/online/bin/online.exe"
OUT="$(pwd)/artifacts/v0.4-smoke/online/out"
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

# online has no positionals → no Windows glob-expansion (W-) section needed.

run S01-help "--help" "$BIN --help"
run S02-version "--version" "$BIN --version"
run S03-describe "--describe" "$BIN --describe"
run S04-once "is the internet up right now (expect 0 on networked runners)" "$BIN --once"
run S05-url-204 "wait for a known 204 endpoint (expect 0)" "$BIN --url https://www.gstatic.com/generate_204 --status 204 --once"
run S06-timeout "unreachable host times out (expect 124)" "$BIN --url https://192.0.2.1/health --timeout 3s --interval 1s --probe-timeout 1s"
run S07-badstatus "invalid --status (expect 125)" "$BIN --status bogus --once"
run S08-endpoint-no-internet "--endpoint without --internet (expect 125)" "$BIN --endpoint https://x.example/generate_204 --url https://y.example/h --once"
run S09-json "--json envelope to stdout (expect 0)" "$BIN --once --json"

echo "=== Smoke run complete ==="
