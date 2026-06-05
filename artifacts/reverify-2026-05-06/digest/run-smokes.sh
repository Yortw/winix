#!/bin/bash
# Re-verification smoke suite for digest.
# Run from repo root: bash artifacts/reverify-2026-05-06/digest/run-smokes.sh
# Captures stdout, stderr, exit code per smoke into ./out/ subdir.

set +e  # don't exit on smoke failure — we want to capture all of them

BIN="$(pwd)/artifacts/reverify-2026-05-06/digest/bin/digest.exe"
OUT="$(pwd)/artifacts/reverify-2026-05-06/digest/out"
DATA="$(pwd)/artifacts/reverify-2026-05-06/digest/data"
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

# Setup test data
echo -n "hello world" > "$DATA/hello.txt"
echo -n "" > "$DATA/empty.txt"
# 50MB of incompressible random data for stress smoke (S13)
head -c 52428800 /dev/urandom > "$DATA/random50mb.bin"
# Path-with-space
mkdir -p "$DATA/dir with space"
echo -n "spacey" > "$DATA/dir with space/file with space.txt"

# Baseline 12
run S01-help "--help exits 0" "$BIN --help"
run S02-version "--version exits 0" "$BIN --version"
run S03-describe "--describe exits 0 with JSON" "$BIN --describe"
run S04-happypath "sha256 of 'hello world' file" "$BIN --sha256 \"$DATA/hello.txt\""
run S05-empty "sha256 of empty file via stdin" "$BIN --sha256 < \"$DATA/empty.txt\""
run S06a-exit0-verify-match "exit 0 on --verify match" "$BIN --sha256 -s 'hello' --verify '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824'"
run S06b-exit1-verify-mismatch "exit 1 on --verify mismatch" "$BIN --sha256 -s 'hello' --verify 'deadbeef'"
run S06c-exit125-badflag "exit 125 on unknown flag" "$BIN --not-a-flag"
run S06d-exit126-filenotfound "exit 126 on missing file" "$BIN --sha256 \"$DATA/does-not-exist.bin\""
run S07-json "--json valid shape" "$BIN --sha256 --json -s 'hello'"
run S08a-stdout-only "stdout-only routing (2>nul)" "$BIN --sha256 -s 'hello' 2>/dev/null"
run S08b-stderr-only "stderr-only routing (1>nul on warning)" "$BIN --md5 -s 'hello' 1>/dev/null"
run S09a-no-color-flag "--no-color flag" "$BIN --sha256 --no-color -s 'hello'"
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN --sha256 -s 'hello'"
run S09c-color-force "--color forces colour" "$BIN --sha256 --color -s 'hello'"
run S10-pipe "stdin pipe-mode" "echo -n 'hello world' | $BIN --sha256"
run S11a-missing-key "--hmac without key source" "$BIN --hmac sha256 -s 'data'"
run S11b-bad-algo "--algo bogus" "$BIN --algo bogus -s 'hello'"
run S12-pathspace "path with space" "$BIN --sha256 \"$DATA/dir with space/file with space.txt\""

# digest-specific category extensions
run S13-large50mb "50MB random file hash sha256" "$BIN --sha256 \"$DATA/random50mb.bin\""
run S14a-sha384 "sha384 hasher" "$BIN --sha384 -s 'hello'"
run S14b-sha512 "sha512 hasher" "$BIN --sha512 -s 'hello'"
run S14c-sha1 "sha1 legacy hasher (warning expected)" "$BIN --sha1 -s 'hello'"
run S14d-md5 "md5 legacy hasher (warning expected)" "$BIN --md5 -s 'hello'"
run S14e-blake2b "blake2b hasher" "$BIN --blake2b -s 'hello'"
run S14f-sha3-256 "sha3-256 (expect 126 on Win .NET)" "$BIN --sha3-256 -s 'hello'"
run S15-hmac-emptykey "HMAC empty-key reject (round-1 C1)" "$BIN --hmac sha256 --key '' -s 'data'"
run S16-hmac-keyfile "HMAC --key-file" "echo -n 'secret' > \"$DATA/key.bin\"; $BIN --hmac sha256 --key-file \"$DATA/key.bin\" -s 'data'"
run S17-hmac-keyenv "HMAC --key-env" "MY_KEY='secret' $BIN --hmac sha256 --key-env MY_KEY -s 'data'"
run S18a-base64 "base64 output" "$BIN --sha256 --base64 -s 'hello'"
run S18b-base64url "base64-url output" "$BIN --sha256 --base64-url -s 'hello'"
run S18c-base32 "Crockford base32 output (round-2 fix)" "$BIN --sha256 --base32 -s 'hello'"
run S18d-uppercase "uppercase hex" "$BIN --sha256 -u -s 'hello'"
run S19-string-mode "--string mode" "$BIN --sha256 -s 'literal value'"
run S20-stdin-binary "binary stdin (round-2 fix)" "head -c 1024 /dev/urandom | $BIN --sha256"
run S21-verify-with-stdin "--verify with stdin happy path" "echo -n 'hello' | $BIN --sha256 --verify '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824'"

# ─── W: Windows glob expansion (Windows-only; skipped on Linux/macOS) ───
IS_WINDOWS=false
case "$(uname -s)" in MINGW*|CYGWIN*|MSYS*) IS_WINDOWS=true ;; esac

if $IS_WINDOWS; then
  GLOB_DIR="$DATA/glob-fixture"
  rm -rf "$GLOB_DIR"
  mkdir -p "$GLOB_DIR"
  echo -n "aaa" > "$GLOB_DIR/a.txt"
  echo -n "bbb" > "$GLOB_DIR/b.txt"

  # W01: glob *.txt expands — tool receives literal pattern and expands it → two hash lines, exit 0
  # Subshell cd prevents MSYS-style absolute path (/d/...) from reaching the tool; the tool
  # sees only the relative bare pattern *.txt and expands it against its cwd.
  run W01-glob-expand "glob *.txt expands to two files (exit 0)" "( cd '$GLOB_DIR' && '$BIN' --sha256 '*.txt' )"

  # W02: quoted pattern via cmd.exe suppresses tool-side expansion → not-found, exit 125
  # Must go via cmd.exe: bash expands *; pwsh strips quotes. We write a temporary .cmd batch
  # so the cmd //c invocation avoids inline \" quoting issues — those reach cmd literally and
  # produce "is not recognized" errors.
  echo "=== W02-glob-quoted: quoted *.txt via cmd.exe suppresses expansion ==="
  GLOB_DIR_WIN="$(cygpath -w "$GLOB_DIR")"
  BIN_WIN="$(cygpath -w "$BIN")"
  W02CMD="$(mktemp --suffix=.cmd)"
  printf '@echo off\r\ncd /d "%s"\r\n"%s" --sha256 "*.txt"\r\n' "$GLOB_DIR_WIN" "$BIN_WIN" > "$W02CMD"
  W02CMD_WIN="$(cygpath -w "$W02CMD")"
  cmd //c "$W02CMD_WIN" \
    >"$OUT/W02-glob-quoted.stdout" 2>"$OUT/W02-glob-quoted.stderr"
  echo $? >"$OUT/W02-glob-quoted.exitcode"
  rm -f "$W02CMD"
  echo "  exit=$(cat "$OUT/W02-glob-quoted.exitcode")  stdout=$(wc -c <"$OUT/W02-glob-quoted.stdout")B  stderr=$(wc -c <"$OUT/W02-glob-quoted.stderr")B"

  # W03: ** → usage error, exit 125
  # Subshell-relative: avoids MSYS-path passthrough; tool sees literal **/*.txt from cwd.
  run W03-double-star "** rejected with usage error (exit 125)" "( cd '$GLOB_DIR' && '$BIN' --sha256 '**/*.txt' )"

  # W04: no-match pattern → literal passthrough → not-found, exit 125
  # Subshell-relative: avoids MSYS-path passthrough; tool sees literal *.nope from cwd.
  run W04-no-match "*.nope no-match → not-found (exit 125)" "( cd '$GLOB_DIR' && '$BIN' --sha256 '*.nope' )"
else
  echo "=== W: Windows glob expansion — SKIPPED (not Windows) ==="
fi

echo
echo "=== Smoke run complete. Outputs in: $OUT ==="
ls "$OUT" | wc -l
echo "Total smoke files (expecting 3 per smoke: cmd, stdout, stderr, exitcode = 4 each)"
