#!/bin/bash
# Category-aware smoke suite for clip.
# Run from repo root: bash artifacts/round-stop-2026-05-09/clip/run-smokes.sh
# Captures stdout, stderr, exit code, cmd per smoke into ./out/ subdir.
# WARNING: this mutates your system clipboard. Suite saves+restores best-effort.

set +e
BIN="$(pwd)/artifacts/round-stop-2026-05-09/clip/fresh-publish/clip.exe"
OUT="$(pwd)/artifacts/round-stop-2026-05-09/clip/out"
DATA="$(pwd)/artifacts/round-stop-2026-05-09/clip/data"
mkdir -p "$OUT" "$DATA"

# Save the user's clipboard before running (best-effort restore at end).
"$BIN" -p > "$DATA/_user_clipboard.bak" 2>/dev/null

run() {
  local id="$1"; local desc="$2"; shift 2
  local cmd="$*"
  echo "=== $id: $desc ==="
  echo "CMD: $cmd" > "$OUT/$id.cmd"
  eval "$cmd" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo $? > "$OUT/$id.exitcode"
  echo "  exit=$(cat "$OUT/$id.exitcode")  stdout=$(wc -c < "$OUT/$id.stdout")B  stderr=$(wc -c < "$OUT/$id.stderr")B"
}

# ─── Baseline 12 ───
run S01-help "--help exits 0" "$BIN --help"
run S02-version "--version exits 0" "$BIN --version"
run S03-describe "--describe exits 0 with JSON" "$BIN --describe"

# Happy path round-trip
run S04-happypath "copy 'test' then paste" "printf 'test' | $BIN -c && $BIN -p"

# Empty stdin → empty clipboard
run S05-empty-copy "empty stdin copy + paste" "printf '' | $BIN -c && $BIN -p"

# Exit codes
run S06a-exit0 "exit 0 happy" "printf 'x' | $BIN -c"
run S06b-exit125-cp-conflict "exit 125: -c -p conflict" "printf 'x' | $BIN -c -p"
run S06c-exit125-c-clear-conflict "exit 125: -c --clear conflict" "printf 'x' | $BIN -c --clear"

# JSON / describe
run S07-describe-json "describe JSON has exit_codes" "$BIN --describe"

# Stream routing
run S08a-paste-stdout-only "paste output to stdout, not stderr" "printf 'A' | $BIN -c && $BIN -p 2>/dev/null"
run S08b-error-stderr "mode-conflict error to stderr (1>nul)" "printf 'x' | $BIN -c -p 1>/dev/null"

# Colour
run S09a-no-color "--no-color flag" "printf 'x' | $BIN -c --no-color"
run S09b-no-color-env "NO_COLOR env" "printf 'x' | NO_COLOR=1 $BIN -c"
run S09c-color-force "--color force" "printf 'x' | $BIN -c --color"

# Pipe-mode
run S10-pipe-roundtrip "stdin pipe-mode round-trip via clipboard" "printf 'pipe-test' | $BIN -c && $BIN -p"

# Bad flag
run S11-bad-flag "unknown flag → 125" "$BIN --not-a-flag"

# (S12 path-with-space — N/A, no path inputs in clip)

# ─── Category extensions ───

# C13 — newline asymmetry contract (clip's most distinctive UX)
run C13a-strip-lf "copy 'foo\\n' paste default → 'foo' (no \\n)" "printf 'foo\n' | $BIN -c && $BIN -p"
run C13b-raw-keeps-lf "copy 'foo\\n' paste --raw → 'foo\\n'" "printf 'foo\n' | $BIN -c && $BIN -p -r"
run C13c-strip-crlf "copy 'foo\\r\\n' paste default → 'foo'" "printf 'foo\r\n' | $BIN -c && $BIN -p"
run C13d-strip-only-one "copy 'foo\\n\\n' paste default → 'foo\\n' (only ONE stripped)" "printf 'foo\n\n' | $BIN -c && $BIN -p"
run C13e-copy-byte-preserve "copy 'foo' paste → 'foo' (no \\n added)" "printf 'foo' | $BIN -c && $BIN -p"

# C14 — clear
run C14a-clear-then-paste "clear then paste → empty" "$BIN --clear && $BIN -p"
run C14b-copy-clear-paste "copy then clear then paste → empty" "printf 'x' | $BIN -c && $BIN --clear && $BIN -p"

# C15 — empty paste
run C15-empty-paste "after clear, paste → exit 0 + empty" "$BIN --clear && $BIN -p"

# C16 — Unicode round-trips
run C16a-latin1 "round-trip 'café résumé naïve'" "printf 'café résumé naïve' | $BIN -c && $BIN -p"
run C16b-cjk "round-trip 'こんにちは 世界'" "printf 'こんにちは 世界' | $BIN -c && $BIN -p"
run C16c-emoji "round-trip '🌏 emoji ✅'" "printf '🌏 emoji ✅' | $BIN -c && $BIN -p"
run C16d-pure-surrogates "round-trip mathematical bold (𝕏 𝕐 𝕑)" "printf '𝕏 𝕐 𝕑' | $BIN -c && $BIN -p"
run C16e-mixed-scripts "round-trip mixed: 'café 🌏 中文 X'" "printf 'café 🌏 中文 X' | $BIN -c && $BIN -p"

# C17 — large payload
run C17a-100kb "100KB ASCII round-trip" "head -c 102400 /dev/urandom | base64 -w0 | head -c 102400 | $BIN -c && $BIN -p | wc -c"
run C17b-1mb "1MB ASCII round-trip — wc -c on paste" "head -c 1048576 /dev/urandom | base64 -w0 | head -c 1048576 | $BIN -c && $BIN -p | wc -c"
run C17c-10kb-emoji "10KB UTF-8 with periodic emoji" "python -c \"import sys; sys.stdout.write(('A'*99 + chr(127757))*100)\" 2>/dev/null | $BIN -c && $BIN -p | wc -c"

# C18 — interior control chars
run C18a-interior-newlines "interior \\n preserved" "printf 'line1\nline2\nline3' | $BIN -c && $BIN -p"
run C18b-tabs "interior tabs preserved" "printf 'col1\tcol2\tcol3' | $BIN -c && $BIN -p"
run C18c-mixed-line-endings "mixed CR/LF interior" "printf 'a\r\nb\nc\r\nd' | $BIN -c && $BIN -p"

# C19 — invalid UTF-8
run C19-invalid-utf8 "invalid UTF-8 bytes copy → 125 per docs" "printf '\xff\xfe\xfd' | $BIN -c"

# C20 — Git Bash autodetect quirk pin
run C20a-bare-no-flag "bare 'clip' under Git Bash — what happens?" "$BIN"

# C21 — mode-conflict matrix
run C21a-cp "-c -p → 125" "printf 'x' | $BIN -c -p"
run C21b-c-clear "-c --clear → 125" "printf 'x' | $BIN -c --clear"
run C21c-p-clear "-p --clear → 125" "$BIN -p --clear"
run C21d-cp-clear "-c -p --clear → 125" "printf 'x' | $BIN -c -p --clear"

# C22 — --primary on Windows
run C22-primary-win "--primary on Windows: silently ignored, copy still works" "printf 'primary-test' | $BIN -c --primary && $BIN -p"

# C23 — special characters
run C23a-null-byte "round-trip null byte (U+0000 valid in UTF-8)" "printf 'a\x00b' | $BIN -c && $BIN -p | xxd"
run C23b-ansi-escape "round-trip ANSI escape sequence" "printf '\x1b[31mred\x1b[0m' | $BIN -c && $BIN -p"

# C24 — --describe payload parity
run C24a-describe-exit-codes "describe enumerates all 4 exit codes" "$BIN --describe"
run C24b-describe-flags "describe lists all flag names" "$BIN --describe"

# Restore user's clipboard (best-effort).
if [ -s "$DATA/_user_clipboard.bak" ]; then
  cat "$DATA/_user_clipboard.bak" | "$BIN" -c
else
  "$BIN" --clear
fi

echo
echo "=== Smoke run complete. Outputs in: $OUT ==="
ls "$OUT" | wc -l
echo "(4 files per smoke: cmd, stdout, stderr, exitcode)"
