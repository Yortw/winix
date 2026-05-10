#!/bin/bash
set +e
BIN="$(pwd)/artifacts/reverify-2026-05-06/when/bin/when.exe"
OUT="$(pwd)/artifacts/reverify-2026-05-06/when/out"
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
run S04-iso-input "ISO timestamp input" "$BIN '2026-05-06T10:00:00Z'"
run S05-empty "no args - shows current time" "$BIN"
run S06a-exit0 "happy path" "$BIN '2026-05-06T10:00:00Z'"
run S06b-exit125-bad-input "bad input -> 125" "$BIN 'not a date'"
run S06c-exit125-bad-tz "unknown timezone -> 125" "$BIN '2026-05-06T10:00:00Z' --tz 'Mars/Olympus_Mons'"
run S07-json "--json shape" "$BIN --json '2026-05-06T10:00:00Z'"
run S08a-stdout "stdout-only routing" "$BIN '2026-05-06T10:00:00Z' 2>/dev/null"
run S08b-stderr "stderr on error" "$BIN 'bogus' 1>/dev/null"
run S09a-no-color "--no-color" "$BIN --no-color '2026-05-06T10:00:00Z'"
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN '2026-05-06T10:00:00Z'"
run S09c-color "--color force" "$BIN --color '2026-05-06T10:00:00Z'"
run S10-pipe "stdin pipe-mode" "echo '2026-05-06T10:00:00Z' | $BIN"
run S11-missing-arg "diff missing arg" "$BIN diff '2026-05-06'"
run S12-pathspace "input with embedded space" "$BIN '2026-05-06 10:00:00'"

# when-specific category
run S13-epoch-input "epoch seconds" "$BIN 1741516800"
run S13b-epoch-millis "epoch millis (heuristic)" "$BIN 1741516800000"
run S14a-1900-boundary "1900 boundary year" "$BIN '1900-01-01T00:00:00Z'"
run S14b-2200-boundary "2200 boundary year" "$BIN '2200-01-01T00:00:00Z'"
run S14c-very-old "year 1800 (out of supported range?)" "$BIN '1800-01-01T00:00:00Z'"
run S14d-very-far "year 9999" "$BIN '9999-12-31T23:59:59Z'"
run S15a-bare-year "+2025 (round-2 SFH-I1 bypass)" "$BIN '+2025'"
run S15b-bare-year-decimal "2025.0 (round-2 SFH-I1)" "$BIN '2025.0'"
run S15c-bare-year-negative "-2025 (round-2 SFH-I1)" "$BIN '-2025'"
run S16-iso-duration-overflow "huge ISO duration P10000Y (round-1 CR-C1)" "$BIN '2026-01-01T00:00:00Z' '+P10000Y'"
run S17a-combined-shorthand "combined +2h30m (round-2 DOCS-C1 fix)" "$BIN '2026-05-06T10:00:00Z' '+2h30m'"
run S17b-combined-1d12h "combined +1d12h (round-2 DOCS-C1 fix)" "$BIN '2026-05-06T10:00:00Z' '+1d12h'"
run S18-tz-tokyo "--tz Asia/Tokyo" "$BIN '2026-05-06T10:00:00Z' --tz 'Asia/Tokyo'"
run S19-diff-mode "diff between two timestamps" "$BIN diff '2026-05-06T10:00:00Z' '2026-05-06T12:30:00Z'"
run S20-diff-iso "diff --iso for ISO 8601 duration output" "$BIN --iso diff '2026-05-06T10:00:00Z' '2026-05-06T12:30:00Z'"
run S21-tz-with-mixed-flags "--tz Asia/Tokyo + negative offset (round-2 partition)" "$BIN '2026-05-06T10:00:00Z' --tz 'Asia/Tokyo' '-3h'"
run S22-utc-flag "--utc for UTC-only output" "$BIN --utc '2026-05-06T10:00:00Z'"
run S23-local-flag "--local for local-only output" "$BIN --local '2026-05-06T10:00:00Z'"

echo "=== Smoke run complete ==="
