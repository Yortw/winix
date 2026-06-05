#!/usr/bin/env bash
# Baseline-suite runner for less tool. Captures stdout/stderr/exit per smoke.
# Interactive smokes (M*) are NOT run from this script — see SUITE-DESIGN.md.

set -u

ART="d:/projects/winix/artifacts/round-stop-2026-05-09/less"
LESS_EXE="$ART/fresh-publish/less.exe"
RES="$ART/results"
mkdir -p "$RES"

if [ ! -x "$LESS_EXE" ]; then
  echo "FATAL: $LESS_EXE not found or not executable" >&2
  exit 1
fi

# Each smoke is wrapped in `timeout 5s` because less is interactive and any smoke that
# enters the pager loop (content larger than viewHeight) will hang waiting for keystrokes.
# Timeout-killed smokes capture exit code 124 (GNU coreutils timeout) which is recorded
# along with whatever output reached stdout/stderr before the kill.
SMOKE_TIMEOUT=5

smoke() {
  local id="$1" ; shift
  local desc="$1" ; shift
  if [ "$1" != "--" ]; then echo "missing -- separator" >&2; return 2; fi
  shift
  echo "[$id] $desc"
  timeout "${SMOKE_TIMEOUT}s" "$@" > "$RES/$id.stdout.txt" 2> "$RES/$id.stderr.txt"
  echo "$?" > "$RES/$id.exit.txt"
}

smoke_stdin() {
  local id="$1" ; shift
  local desc="$1" ; shift
  local stdin_file="$1" ; shift
  if [ "$1" != "--" ]; then echo "missing -- separator" >&2; return 2; fi
  shift
  echo "[$id] $desc (stdin from $stdin_file)"
  timeout "${SMOKE_TIMEOUT}s" "$@" < "$stdin_file" > "$RES/$id.stdout.txt" 2> "$RES/$id.stderr.txt"
  echo "$?" > "$RES/$id.exit.txt"
}

smoke_env() {
  local id="$1" ; shift
  local desc="$1" ; shift
  local envvars="$1" ; shift
  if [ "$1" != "--" ]; then echo "missing -- separator" >&2; return 2; fi
  shift
  echo "[$id] $desc (env: $envvars)"
  timeout "${SMOKE_TIMEOUT}s" env $envvars "$@" > "$RES/$id.stdout.txt" 2> "$RES/$id.stderr.txt"
  echo "$?" > "$RES/$id.exit.txt"
}

# ----- Synthesise inputs -----
TMP="$ART/tmp"
mkdir -p "$TMP"

# Tiny one-liner — fits any screen, exercises -F default
echo "Hello from less baseline" > "$TMP/tiny.txt"

# A 5-line file
{
  echo "line one"
  echo "line two"
  echo "line three"
  echo "line four"
  echo "line five"
} > "$TMP/short.txt"

# Empty file
> "$TMP/empty.txt"

# File with ANSI codes
printf '\x1b[31mRed text\x1b[0m\n\x1b[32mGreen text\x1b[0m\n' > "$TMP/ansi.txt"

# File with UTF-8 BOM
printf '\xEF\xBB\xBFLine after BOM\n' > "$TMP/bom.txt"

# Synth a 10MB file for memory probe (10 million chars)
perl -e 'print "x" x 10000000' > "$TMP/huge.txt" 2>/dev/null || python -c "import sys; sys.stdout.write('x' * 10000000)" > "$TMP/huge.txt"

# A "second file" for multi-file probe
echo "second file content" > "$TMP/second.txt"

# Long-line file (80 KB single line, wraps when displayed)
perl -e 'print "y" x 80000' > "$TMP/longline.txt" 2>/dev/null || python -c "import sys; sys.stdout.write('y' * 80000)" > "$TMP/longline.txt"

# A directory used as input
mkdir -p "$TMP/somedir"

# 1 KB binary file (random bytes from urandom)
head -c 1024 /dev/urandom > "$TMP/binary.bin"

# Mixed line endings (CRLF + LF)
printf 'crlf-line\r\nlf-line\nplain-end' > "$TMP/mixed-eol.txt"

# Latin-1 (Windows-1252) content with é (0xE9 byte)
printf 'caf\xE9 latte\n' > "$TMP/latin1.txt"

# ----- B: Standard baseline -----

smoke B01 "--version" -- "$LESS_EXE" --version
smoke B02 "--help" -- "$LESS_EXE" --help
smoke B03 "--describe" -- "$LESS_EXE" --describe
smoke B04 "no args, no stdin" -- "$LESS_EXE"
smoke B05 "unknown flag" -- "$LESS_EXE" --invalid-flag "$TMP/tiny.txt"
smoke B06 "small file (-F default fits)" -- "$LESS_EXE" "$TMP/tiny.txt"
smoke_env B07 "NO_COLOR + ansi file" "NO_COLOR=1" -- "$LESS_EXE" "$TMP/ansi.txt"
smoke B08 "--json on a file" -- "$LESS_EXE" --json "$TMP/tiny.txt"
smoke B09 "--color force" -- "$LESS_EXE" --color "$TMP/ansi.txt"
smoke B10 "--no-color" -- "$LESS_EXE" --no-color "$TMP/ansi.txt"
smoke B11 "extra positional" -- "$LESS_EXE" "$TMP/tiny.txt" extra-arg
smoke_stdin B12 "echo piped (small fits)" "$TMP/tiny.txt" -- "$LESS_EXE"

# ----- I: Input source -----

smoke I01 "existing small file" -- "$LESS_EXE" "$TMP/short.txt"
smoke I02 "missing file" -- "$LESS_EXE" "$TMP/does-not-exist.txt"
smoke I03 "dash filename (POSIX stdin)" -- "$LESS_EXE" "-"
smoke I04 "directory as input" -- "$LESS_EXE" "$TMP/somedir"
smoke_stdin I05 "stdin pipe small" "$TMP/short.txt" -- "$LESS_EXE"
smoke I06 "huge file 10 MB (memory probe)" -- "$LESS_EXE" "$TMP/huge.txt"
smoke_stdin I07 "binary stdin" "$TMP/binary.bin" -- "$LESS_EXE"
smoke I08 "empty file" -- "$LESS_EXE" "$TMP/empty.txt"
smoke I09 "two files" -- "$LESS_EXE" "$TMP/short.txt" "$TMP/second.txt"

# ----- L: LESS env var -----

smoke_env L01 "no LESS env" "_=1" -- "$LESS_EXE" "$TMP/short.txt"
smoke_env L02 "LESS empty string" "LESS=" -- "$LESS_EXE" "$TMP/short.txt"
smoke_env L03 "LESS=NiR" "LESS=NiR" -- "$LESS_EXE" "$TMP/ansi.txt"
smoke_env L04 "LESS=Z (unknown char)" "LESS=Z" -- "$LESS_EXE" "$TMP/short.txt"
smoke_env L05 "LESS=NN (duplicate)" "LESS=NN" -- "$LESS_EXE" "$TMP/short.txt"
smoke_env L06 "LESS=N + CLI -F" "LESS=N" -- "$LESS_EXE" -F "$TMP/short.txt"

# ----- P: +command parsing -----

smoke P03 "+/pattern with small file" -- "$LESS_EXE" "+/three" "$TMP/short.txt"
smoke P04 "bare + as positional" -- "$LESS_EXE" "+" "$TMP/short.txt"
smoke P05 "just + with no file" -- "$LESS_EXE" "+"
smoke P07 "multiple +commands" -- "$LESS_EXE" "+/three" "+G" "$TMP/short.txt"

# ----- Q: Quit-if-one-screen -----

smoke Q01 "1 line content -F default dump" -- "$LESS_EXE" "$TMP/tiny.txt"
smoke_env Q05 "LESS=N (no F) on tiny — should enter pager (will time out)" "LESS=N" -- "$LESS_EXE" "$TMP/tiny.txt"
smoke Q06 "long-line wraps to fill (will time out)" -- "$LESS_EXE" "$TMP/longline.txt"

# ----- C: Colour -----

smoke C01 "ansi file default" -- "$LESS_EXE" "$TMP/ansi.txt"
smoke_env C02 "ansi + NO_COLOR" "NO_COLOR=1" -- "$LESS_EXE" "$TMP/ansi.txt"
smoke C04 "ansi + --color" -- "$LESS_EXE" --color "$TMP/ansi.txt"
smoke C05 "ansi + --no-color" -- "$LESS_EXE" --no-color "$TMP/ansi.txt"

# ----- E: Error handling -----

smoke E01 "non-existent file" -- "$LESS_EXE" "$TMP/none-here.log"
smoke E04 "directory as input (dup of I04)" -- "$LESS_EXE" "$TMP/somedir"
smoke E06 "filename with spaces" -- "$LESS_EXE" "$TMP/non existent file with spaces.txt"

# ----- G: InvariantGlobalization probes -----

# G03: invalid Win32 chars in filename — ?* characters
smoke G03 "filename with ?*" -- "$LESS_EXE" "$TMP/wild?.txt"

# ----- U: Unicode / encoding -----

smoke U01 "UTF-8 BOM file" -- "$LESS_EXE" "$TMP/bom.txt"
smoke U02 "Latin-1 file" -- "$LESS_EXE" "$TMP/latin1.txt"
smoke U03 "very long single line" -- "$LESS_EXE" "$TMP/longline.txt"
smoke_stdin U04 "mixed CRLF/LF stdin" "$TMP/mixed-eol.txt" -- "$LESS_EXE"
smoke_stdin U05 "empty stdin pipe" /dev/null -- "$LESS_EXE"

# Manual section noted in artifact
echo "[M01-M08 SKIPPED] Interactive — require TTY harness; see SUITE-DESIGN.md M section." > "$RES/M_NOTE.txt"

# ----- W: Windows glob expansion (Windows-only; skipped on Linux/macOS) -----

IS_WINDOWS=false
case "$(uname -s)" in MINGW*|CYGWIN*|MSYS*) IS_WINDOWS=true ;; esac

if $IS_WINDOWS; then
  WGLOB="$TMP/glob-fixture"
  rm -rf "$WGLOB"
  mkdir -p "$WGLOB"
  echo "ccc" > "$WGLOB/c.log"
  echo "aaa" > "$WGLOB/a.txt"
  echo "bbb" > "$WGLOB/b.txt"

  # W01: *.log expands to single file — less pages/dumps it, exit 0
  # Double-quoted $WGLOB/*.log: bash doesn't glob-expand inside ""; tool receives the literal.
  smoke W01 "*.log expands to single match (exit 0)" -- "$LESS_EXE" "$WGLOB/*.log"

  # W02: *.txt expands to two files — less rejects multi-file positional, exit 2
  smoke W02 "*.txt expands to two files → multi-file error (exit 2)" -- "$LESS_EXE" "$WGLOB/*.txt"

  # W03: ** → usage error, exit 2 (less uses 2 for usage errors per POSIX convention)
  smoke W03 "** rejected with usage error (exit 2)" -- "$LESS_EXE" "$WGLOB/**"

  # W04: no-match pattern → not-found, exit 1
  smoke W04 "*.nope no-match → not-found (exit 1)" -- "$LESS_EXE" "$WGLOB/*.nope"
else
  echo "[W01-W04 SKIPPED] Windows glob expansion — not Windows."
fi

echo
echo "==== Done. Results in $RES/ ===="
ls "$RES" | wc -l
