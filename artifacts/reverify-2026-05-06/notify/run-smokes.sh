#!/bin/bash
# Re-verification smoke suite for notify.
# Note: each S04+ smoke pops a Windows toast notification.

set +e
BIN="$(pwd)/artifacts/reverify-2026-05-06/notify/bin/notify.exe"
OUT="$(pwd)/artifacts/reverify-2026-05-06/notify/out"
DATA="$(pwd)/artifacts/reverify-2026-05-06/notify/data"
mkdir -p "$OUT" "$DATA"

run() {
  local id="$1"; local desc="$2"; shift 2
  local cmd="$*"
  echo "=== $id: $desc ==="
  echo "CMD: $cmd" > "$OUT/$id.cmd"
  eval "$cmd" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo $? > "$OUT/$id.exitcode"
  echo "  exit=$(cat "$OUT/$id.exitcode")  stdout=$(wc -c < "$OUT/$id.stdout")B  stderr=$(wc -c < "$OUT/$id.stderr")B"
}

mkdir -p "$DATA/dir with space"
echo -n "icon" > "$DATA/dir with space/icon.png"

# Baseline 12
run S01-help "--help exits 0" "$BIN --help"
run S02-version "--version exits 0" "$BIN --version"
run S03-describe "--describe valid JSON" "$BIN --describe"
run S04-happypath "TITLE BODY desktop toast" "$BIN 'Smoke S04' 'Re-verify body'"
run S05-empty-title "empty title (positional with empty string)" "$BIN ''"
run S06a-exit0-happy "exit 0 happy" "$BIN 'Smoke S06a' 'body'"
run S06b-exit125-no-title "exit 125 no title" "$BIN"
run S06c-exit125-no-backends "exit 125 no backends configured" "$BIN --no-desktop --no-ntfy 'Title' 'Body'"
run S06d-exit125-too-many-positionals "exit 125 >2 positionals" "$BIN A B C"
run S07-json "--json output shape" "$BIN --json 'Smoke S07' 'json body'"
run S08a-stdout-only "stdout-only routing" "$BIN --json 'Smoke S08a' 'body' 2>/dev/null"
run S08b-stderr-only "stderr-only routing — error to stderr" "$BIN --no-desktop --no-ntfy 'Title' 1>/dev/null"
run S09a-no-color "--no-color flag" "$BIN --no-color 'Smoke S09a' 'body'"
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN 'Smoke S09b' 'body'"
run S09c-color-force "--color forces" "$BIN --color 'Smoke S09c' 'body'"
run S10-no-pipe-mode "stdin not consumed (positional rules)" "echo body | $BIN 'Smoke S10'"
run S11-missing-required "missing TITLE arg" "$BIN --urgency normal"
run S12-pathspace-icon "--icon path with space" "$BIN --icon \"$DATA/dir with space/icon.png\" 'Smoke S12' 'body'"

# notify-specific category
run S13-long-body "very long body 10KB" "$BIN 'Smoke S13' \"\$(printf 'X%.0s' {1..10000})\""
run S14a-unicode "unicode + emoji body" "$BIN 'Smoke 完了 ✅' 'café résumé naïve 🎉'"
run S14b-newlines-body "multi-line body" "$BIN 'Smoke S14b' \$'line1\nline2\nline3'"
run S15-urgency-low "--urgency low" "$BIN --urgency low 'S15 low' 'body'"
run S16-urgency-critical "--urgency critical" "$BIN --urgency critical 'S16 crit' 'body'"
run S17-bad-urgency "--urgency bogus → 125" "$BIN --urgency bogus 'S17' 'body'"
run S18-icon-nonexistent "--icon nonexistent path (best-effort)" "$BIN --icon /nonexistent/path.png 'Smoke S18' 'body'"
run S19-strict-mode "--strict --no-desktop with ntfy disabled → 125" "$BIN --strict --no-desktop --no-ntfy 'Smoke S19' 'body'"
run S20-ntfy-no-topic "--ntfy with no topic value" "$BIN --ntfy 'Smoke S20' 'body'"
run S21-no-desktop-only "--no-desktop, no ntfy env → 125" "$BIN --no-desktop 'Smoke S21' 'body'"

echo
echo "=== Smoke run complete. Outputs in: $OUT ==="
