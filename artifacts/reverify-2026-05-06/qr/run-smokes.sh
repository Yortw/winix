#!/bin/bash
set +e
BIN="$(pwd)/artifacts/reverify-2026-05-06/qr/bin/qr.exe"
OUT="$(pwd)/artifacts/reverify-2026-05-06/qr/out"
DATA="$(pwd)/artifacts/reverify-2026-05-06/qr/data"
mkdir -p "$OUT" "$DATA"
mkdir -p "$DATA/dir with space"

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
run S04-happypath "encode 'hello' to ascii (forced)" "$BIN --format ascii hello"
run S05-empty "no payload arg, no stdin" "$BIN --format ascii"
run S06a-exit0 "exit 0 happy" "$BIN --format ascii hello"
run S06b-exit125-bad-flag "exit 125 bad flag" "$BIN --not-a-flag hello"
run S06c-exit125-bad-subcmd "exit 125 bad subcmd flag" "$BIN wifi --bogus"
run S06d-exit126-too-large "exit 126 payload too large" "$BIN --format ascii \$(printf 'A%.0s' {1..3000})"
run S07-json "--json shape (svg auto)" "$BIN --json --format svg --output \"$DATA/s07.svg\" hello"
run S08a-stdout "ascii to stdout" "$BIN --format ascii hello 2>/dev/null"
run S08b-stderr "stderr on error" "$BIN --not-a-flag 1>/dev/null"
run S09a-no-color "--no-color" "$BIN --no-color --format ascii hello"
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN --format ascii hello"
run S09c-color "--color (no effect on ascii really)" "$BIN --color --format ascii hello"
run S10-pipe "stdin pipe-mode" "echo -n 'piped payload' | $BIN --format ascii"
run S11-missing-required "wifi without --ssid" "$BIN wifi"
run S12-pathspace-output "--output path with space" "$BIN --format svg --output \"$DATA/dir with space/s12.svg\" hello"

# qr-specific category
run S13-max-version40 "max-version-40 payload (~2900 chars alphanumeric, l ECC)" "$BIN -e l --format ascii \$(printf 'A%.0s' {1..2000})"
run S14a-wifi "wifi helper" "$BIN wifi --ssid 'MyNet' --password 'pass1234' --security wpa2 --format ascii"
run S14b-mailto "mailto helper (round-2.5 InvariantGlobalization fix site)" "$BIN mailto --to 'a@b.com' --subject 'Hi' --body 'Test' --format ascii"
run S14c-geo "geo helper (round-2.5 fix site)" "$BIN geo --lat 35.0 --lon 139.0 --format ascii"
run S14d-tel "tel helper" "$BIN tel --number '+12025551234' --format ascii"
run S14e-sms "sms helper" "$BIN sms --number '+12025551234' --message 'Hi' --format ascii"
run S15-mailto-bad-email "mailto with malformed --to" "$BIN mailto --to 'not-an-email' --format ascii"
run S16-tel-letters "tel with letters (round-1 SFH-I3 sanitiser)" "$BIN tel --number 'CALL-NOW' --format ascii"
run S17a-extension-svg-png-mismatch "--format svg with .png extension (SFH-I1)" "$BIN --format svg --output \"$DATA/s17a.png\" hello"
run S17b-no-format-with-extension "no --format, infer from .png extension" "$BIN --output \"$DATA/s17b.png\" hello"
run S18-overwrite-without-force "refuse overwrite without --force (SFH-I2)" "echo existing > \"$DATA/s18.svg\"; $BIN --format svg --output \"$DATA/s18.svg\" hello"
run S19-overwrite-with-force "overwrite with --force" "$BIN --format svg --force --output \"$DATA/s18.svg\" hello"
run S20-empty-output-flag "--output '' (round-2.5 fix)" "$BIN --output '' hello"
run S21-renderer-unicode "unicode renderer to file" "$BIN --format unicode --output \"$DATA/s21.txt\" hello"
run S22-renderer-svg "svg renderer to file" "$BIN --format svg --output \"$DATA/s22.svg\" hello"
run S23-renderer-png "png renderer to file" "$BIN --format png --output \"$DATA/s23.png\" hello"
run S24-png-tty-without-force-binary "png to stdout (TTY) without --force-binary" "$BIN --format png hello"
run S25-error-correction-h "ec=h (high)" "$BIN -e h --format ascii hello"

echo "=== Smoke run complete ==="
