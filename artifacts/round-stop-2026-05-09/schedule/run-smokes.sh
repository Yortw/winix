#!/bin/bash
# Tier-1 smoke suite for schedule (cron parser + schtasks/crontab backend).
set +e
BIN="d:/projects/winix/artifacts/round-stop-2026-05-09/schedule/fresh-publish/schedule.exe"
OUT="d:/projects/winix/artifacts/round-stop-2026-05-09/schedule/out"
mkdir -p "$OUT"

run() {
  local id="$1"; local desc="$2"; shift 2
  echo "=== $id: $desc ==="
  echo "$@" > "$OUT/$id.cmd"
  timeout 10s "$@" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo "$?" > "$OUT/$id.exit"
}

# ── Baseline 12 ──
run B01 "--version" "$BIN" --version
run B02 "--help" "$BIN" --help
run B03 "--describe" "$BIN" --describe
run B04 "no args" "$BIN"
run B05 "unknown subcommand" "$BIN" frobnicate
run B06 "next: valid cron 0 9 * * *" "$BIN" next "0 9 * * *"
run B07 "next: bad cron" "$BIN" next "bogus expression"
run B08 "next: @daily preset" "$BIN" next "@daily"
run B09 "next: 0 9 * * MON" "$BIN" next "0 9 * * MON"
run B10 "list (read-only)" "$BIN" list
run B11 "--no-color list" "$BIN" --no-color list
run B12 "NO_COLOR env list" env NO_COLOR=1 "$BIN" list

# ── Schedule-specific extensions ──
run S01 "next --json" "$BIN" next --json "0 9 * * MON"
run S02 "next --count 5" "$BIN" next --count 5 "@daily"
run S03 "next RRULE" "$BIN" next "FREQ=DAILY;BYHOUR=9"
run S04 "next bare 5-field" "$BIN" next "* * * * *"
run S05 "next malformed (60 minutes)" "$BIN" next "60 * * * *"
run S06 "history --json" "$BIN" history --json

echo
echo "==== schedule done: $(ls $OUT | wc -l) result files ===="
