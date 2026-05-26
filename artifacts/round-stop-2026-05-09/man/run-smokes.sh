#!/usr/bin/env bash
# Baseline-suite runner for man tool — runs each smoke in its own shell, captures stdout/stderr/exit.
# Designed for the 2026-05-07 pre-round-1 baseline. Does NOT apply fixes — observation only.

set -u  # NOT -e: smokes are expected to have non-zero exits sometimes

ART="d:/projects/winix/artifacts/round-stop-2026-05-09/man"
MAN_EXE="$ART/fresh-publish/man.exe"
RES="$ART/results"
mkdir -p "$RES"

# Sanity check
if [ ! -x "$MAN_EXE" ]; then
  echo "FATAL: $MAN_EXE not found or not executable" >&2
  exit 1
fi

# smoke <id> <description> -- <command...>
# Captures stdout/stderr/exit for the command verbatim.
smoke() {
  local id="$1" ; shift
  local desc="$1" ; shift
  # consume the literal "--"
  if [ "$1" != "--" ]; then echo "smoke: missing -- separator after desc" >&2; return 2; fi
  shift
  echo "[$id] $desc"
  "$@" > "$RES/$id.stdout.txt" 2> "$RES/$id.stderr.txt"
  echo "$?" > "$RES/$id.exit.txt"
}

# Variant: pipe stdin from a file
smoke_stdin() {
  local id="$1" ; shift
  local desc="$1" ; shift
  local stdin_file="$1" ; shift
  if [ "$1" != "--" ]; then echo "smoke_stdin: missing -- separator after stdin" >&2; return 2; fi
  shift
  echo "[$id] $desc (stdin from $stdin_file)"
  "$@" < "$stdin_file" > "$RES/$id.stdout.txt" 2> "$RES/$id.stderr.txt"
  echo "$?" > "$RES/$id.exit.txt"
}

# Variant: set env var(s) for the smoke
smoke_env() {
  local id="$1" ; shift
  local desc="$1" ; shift
  local envvars="$1" ; shift  # space-separated KEY=VALUE pairs (no spaces in values)
  if [ "$1" != "--" ]; then echo "smoke_env: missing -- separator after env" >&2; return 2; fi
  shift
  echo "[$id] $desc (env: $envvars)"
  env $envvars "$@" > "$RES/$id.stdout.txt" 2> "$RES/$id.stderr.txt"
  echo "$?" > "$RES/$id.exit.txt"
}

# ----- B: Standard baseline -----

smoke B01 "--version" -- "$MAN_EXE" --version
smoke B02 "--help" -- "$MAN_EXE" --help
smoke B03 "--describe" -- "$MAN_EXE" --describe
smoke B04 "no args" -- "$MAN_EXE"
smoke B05 "unknown flag" -- "$MAN_EXE" --invalid-flag
smoke B06 "piped stdout (file redirect)" -- "$MAN_EXE" man  # outputs go to file via shell redirect, no pager
smoke_env B07 "NO_COLOR + --no-pager" "NO_COLOR=1" -- "$MAN_EXE" --no-pager man
smoke B08 "--no-pager man" -- "$MAN_EXE" --no-pager man
smoke B09 "--color --no-pager (forced colour through pipe)" -- "$MAN_EXE" --color --no-pager man
smoke B10 "--json man" -- "$MAN_EXE" --json --no-pager man
smoke B11 "extra positional args" -- "$MAN_EXE" man extra junk args
smoke B12 "--width 40 --no-pager man" -- "$MAN_EXE" --width 40 --no-pager man

# ----- R: Page resolution -----

smoke R01 "find own bundled page (man)" -- "$MAN_EXE" --no-pager man
smoke R02 "--path man" -- "$MAN_EXE" --path man
smoke R03 "--manpath" -- "$MAN_EXE" --manpath
smoke R04 "page not found" -- "$MAN_EXE" does-not-exist-anywhere
smoke R05 "invalid section number" -- "$MAN_EXE" 99 man
smoke R06 "negative section number" -- "$MAN_EXE" -1 man
# R07/R08: MANPATH manipulation
TMPDIR_EMPTY="$ART/tmp/empty-manpath"
mkdir -p "$TMPDIR_EMPTY"
smoke_env R07 "MANPATH=empty-dir" "MANPATH=$TMPDIR_EMPTY" -- "$MAN_EXE" --no-pager man

TMPDIR_FAKE="$ART/tmp/fake-manpath"
mkdir -p "$TMPDIR_FAKE/man1"
cat > "$TMPDIR_FAKE/man1/fake.1" << 'EOF'
.TH FAKE 1 "2026-05-07" "Test"
.SH NAME
fake \- baseline test page for MANPATH probe
.SH DESCRIPTION
This is a synthesised man page used by the man baseline suite.
EOF
smoke_env R08 "MANPATH=dir-with-fake.1" "MANPATH=$TMPDIR_FAKE" -- "$MAN_EXE" --no-pager fake

smoke R10 "no section, ambiguous" -- "$MAN_EXE" --no-pager man
smoke R11 "--path with section" -- "$MAN_EXE" --path 1 man

# ----- P: Pager chain -----

smoke P01 "--no-pager (no pager invoked)" -- "$MAN_EXE" --no-pager man
# P02: requires pipe redirection of MANPAGER output → cat dumps
# We'll set MANPAGER=cat — but we're running with stdout redirected to file already, so isTerminal=false
# means PagerChain skips the pager entirely. To probe MANPAGER we need stdout to be a terminal.
# This smoke can't really exercise the pager from inside a script unless we use pty. Skip with note.
echo "[P02 SKIPPED] Requires terminal stdout (pty); pager only invoked when isTerminal=true." > "$RES/P02.note.txt"
echo "[P03 SKIPPED] Same reason — MANPAGER='less -R' tokenization probe needs terminal stdout." > "$RES/P03.note.txt"
echo "[P04 SKIPPED] Same reason." > "$RES/P04.note.txt"
echo "[P05 SKIPPED] Same reason." > "$RES/P05.note.txt"

# However we CAN probe by running with explicit terminal-ish state via FORCE flag — but man has no such flag.
# Instead: use a separate harness in PowerShell or use CMD /C with new console. For now mark as needing manual probe.

# P06: stdin-from-/dev/null
smoke_stdin P06 "stdin from null device" /dev/null -- "$MAN_EXE" --no-pager man

# ----- G: Groff input handling -----

# G01: empty .1 file via MANPATH
TMPDIR_EMPTY1="$ART/tmp/groff-empty"
mkdir -p "$TMPDIR_EMPTY1/man1"
> "$TMPDIR_EMPTY1/man1/empty.1"
smoke_env G01 "empty .1 file" "MANPATH=$TMPDIR_EMPTY1" -- "$MAN_EXE" --no-pager empty

# G02: random ASCII (no groff requests)
TMPDIR_ASCII="$ART/tmp/groff-ascii"
mkdir -p "$TMPDIR_ASCII/man1"
echo "Just plain text with no groff macros at all" > "$TMPDIR_ASCII/man1/ascii.1"
smoke_env G02 "plain text no macros" "MANPATH=$TMPDIR_ASCII" -- "$MAN_EXE" --no-pager ascii

# G03: file with UTF-8 BOM
TMPDIR_BOM="$ART/tmp/groff-bom"
mkdir -p "$TMPDIR_BOM/man1"
printf '\xEF\xBB\xBF.TH BOMTEST 1 "2026" "Test"\n.SH NAME\nbomtest \\- testing UTF-8 BOM handling\n' > "$TMPDIR_BOM/man1/bomtest.1"
smoke_env G03 "UTF-8 BOM at start" "MANPATH=$TMPDIR_BOM" -- "$MAN_EXE" --no-pager bomtest

# G04: gzip-compressed page
TMPDIR_GZ="$ART/tmp/groff-gz"
mkdir -p "$TMPDIR_GZ/man1"
cat > "$TMPDIR_GZ/man1/gztest.1" << 'EOF'
.TH GZTEST 1 "2026" "Test"
.SH NAME
gztest \- testing .gz fallback
.SH DESCRIPTION
This page is gzip-compressed.
EOF
gzip "$TMPDIR_GZ/man1/gztest.1"
smoke_env G04 ".gz compressed page" "MANPATH=$TMPDIR_GZ" -- "$MAN_EXE" --no-pager gztest

# G05: gzip with truncated/bad data
TMPDIR_BADGZ="$ART/tmp/groff-badgz"
mkdir -p "$TMPDIR_BADGZ/man1"
printf 'not actually gzip data' > "$TMPDIR_BADGZ/man1/badgz.1.gz"
smoke_env G05 "malformed .gz file" "MANPATH=$TMPDIR_BADGZ" -- "$MAN_EXE" --no-pager badgz

# G06: very long single line (80 KB)
TMPDIR_LONG="$ART/tmp/groff-long"
mkdir -p "$TMPDIR_LONG/man1"
{
  echo '.TH LONGTEST 1 "2026" "Test"'
  echo '.SH DESCRIPTION'
  python -c "import sys; sys.stdout.write('x' * 80000 + '\n')" 2>/dev/null || perl -e 'print "x" x 80000, "\n"'
} > "$TMPDIR_LONG/man1/longtest.1"
smoke_env G06 "very long line (80 KB)" "MANPATH=$TMPDIR_LONG" -- "$MAN_EXE" --no-pager longtest

# G07: control chars in NAME description
TMPDIR_CTRL="$ART/tmp/groff-ctrl"
mkdir -p "$TMPDIR_CTRL/man1"
# Bell (\a / 0x07) inside description text
printf '.TH CTRLTEST 1 "2026" "Test"\n.SH NAME\nctrltest \\- description with control char \x07 here\n' > "$TMPDIR_CTRL/man1/ctrltest.1"
smoke_env G07 "control char in NAME description" "MANPATH=$TMPDIR_CTRL" -- "$MAN_EXE" --json --no-pager ctrltest

# ----- W: Width handling -----

smoke W01 "--width 10 (boundary OK)" -- "$MAN_EXE" --width 10 --no-pager man
smoke W02 "--width 9 (below minimum)" -- "$MAN_EXE" --width 9 --no-pager man
smoke W03 "--width 200 (well above 80)" -- "$MAN_EXE" --width 200 --no-pager man
smoke_env W04 "MANWIDTH=120" "MANWIDTH=120" -- "$MAN_EXE" --no-pager man
smoke_env W05 "MANWIDTH=junk" "MANWIDTH=not-a-number" -- "$MAN_EXE" --no-pager man
smoke W06 "default width on terminal-redirected stdout" -- "$MAN_EXE" --no-pager man

# ----- J: JSON / metadata -----

smoke J01 "--json basic" -- "$MAN_EXE" --json --no-pager man
smoke J02 "describe shape sanity" -- "$MAN_EXE" --describe
smoke J03 "--json for not-found page" -- "$MAN_EXE" --json --no-pager does-not-exist
smoke_env J05 "--json with control char" "MANPATH=$TMPDIR_CTRL" -- "$MAN_EXE" --json --no-pager ctrltest

# ----- I: InvariantGlobalization probes -----

# I01: try a truly unreadable path. On Windows, locking a file open then reading is hard from script.
# Probe: a file with reserved Windows name should trigger a framework path validator.
TMPDIR_RESERVED="$ART/tmp/groff-reserved"
mkdir -p "$TMPDIR_RESERVED/man1"
cat > "$TMPDIR_RESERVED/man1/normal.1" << 'EOF'
.TH NORMAL 1 "2026" "Test"
.SH NAME
normal \- nothing special here
EOF
# I02: page name containing forbidden chars (Windows path validation)
smoke_env I02 "page name with forbidden char (?)" "MANPATH=$TMPDIR_RESERVED" -- "$MAN_EXE" --no-pager 'na?me'

# I03: page name with spaces
smoke_env I03 "page name with spaces" "MANPATH=$TMPDIR_RESERVED" -- "$MAN_EXE" --no-pager "na me"

# I04: gzip with bad CRC — duplicate of G05, but routed through I04 for clarity
TMPDIR_BADCRC="$ART/tmp/groff-badcrc"
mkdir -p "$TMPDIR_BADCRC/man1"
# Real gzip header bytes followed by random data — header parses, decompress fails
printf '\x1f\x8b\x08\x00\x00\x00\x00\x00\x00\x00garbage_data_not_compressed_correctly' > "$TMPDIR_BADCRC/man1/badcrc.1.gz"
smoke_env I04 "gzip with valid header but corrupt body" "MANPATH=$TMPDIR_BADCRC" -- "$MAN_EXE" --no-pager badcrc

echo
echo "==== Done. Results in $RES/ ===="
ls "$RES" | wc -l
