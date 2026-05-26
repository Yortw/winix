#!/bin/bash
set +e
BIN="$(pwd)/artifacts/reverify-2026-05-06/timeit/bin/timeit.exe"
OUT="$(pwd)/artifacts/reverify-2026-05-06/timeit/out"
DATA="$(pwd)/artifacts/reverify-2026-05-06/timeit/data"
mkdir -p "$OUT" "$DATA"
mkdir -p "$DATA/dir with space"

# Make a tiny exe-like that just exits N for exit-code passthrough tests
cat > "$DATA/exit-with.sh" <<'EOF'
#!/bin/bash
exit "${1:-0}"
EOF
chmod +x "$DATA/exit-with.sh"

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
run S04-happypath "time a fast command" "$BIN cmd //c \"echo hello\""
run S05-empty "no command at all" "$BIN"
run S06a-exit0 "child exit 0 passthrough" "$BIN cmd //c \"exit 0\""
run S06b-exit1 "child exit 1 passthrough" "$BIN cmd //c \"exit 1\""
run S06c-exit2 "child exit 2 passthrough" "$BIN cmd //c \"exit 2\""
run S06d-exit125-no-cmd "no command -> 125" "$BIN"
run S06e-exit127-not-found "command not found -> 127" "$BIN no-such-binary-12345"
run S07-json "--json shape" "$BIN --json cmd //c \"echo hello\""
run S08a-stdout-summary "--stdout writes summary to stdout" "$BIN --stdout cmd //c \"echo hello\""
run S08b-default-stderr "default summary on stderr" "$BIN cmd //c \"echo hello\" 1>/dev/null"
run S09a-no-color "--no-color flag" "$BIN --no-color cmd //c \"exit 0\""
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN cmd //c \"exit 0\""
run S09c-color "--color force" "$BIN --color cmd //c \"exit 0\""
run S10-pipe-passthrough "child output passes through" "$BIN cmd //c \"echo PIPE-PASS\""
run S11-double-dash "--  separator works" "$BIN -- cmd //c \"echo --separator-ok\""
run S12-pathspace "command path with space (--)" "$BIN -- \"$DATA/dir with space\"/no-such 2>&1"

# timeit-specific category extensions
run S13-exit-codes-passthrough-many "exit 130 passthrough" "$BIN cmd //c \"exit 130\""
run S13b-exit-255 "exit 255 passthrough" "$BIN cmd //c \"exit 255\""
run S14-stdin-inheritance "child reads stdin (timeit must inherit)" "echo MARKER | $BIN cmd //c \"more\""
run S15-stderr-of-child "child writes to stderr — captured separately" "$BIN cmd //c \"echo TOSTDERR 1>&2\""
run S16-oneline "--oneline format" "$BIN --oneline cmd //c \"exit 0\""
run S17-bad-exe-on-windows "bad exe (passing a directory) -> 126" "$BIN \"$DATA\""
run S18-empty-command-arg "empty string as command -> 125" "$BIN \"\""
run S19-many-args "many positional args" "$BIN cmd //c \"echo a b c d e f g\""
run S20-very-fast-cmd "command that exits in <1ms" "$BIN cmd //c \"exit 0\""

echo "=== Smoke run complete ==="
