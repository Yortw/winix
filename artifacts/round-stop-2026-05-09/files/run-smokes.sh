#!/bin/bash
# Category-aware smoke suite for files.
# Run from repo root: bash artifacts/round-stop-2026-05-09/files/run-smokes.sh
# Reuses treex's fixture-tree shape (deterministic against ./data/fixture).

set +e
BIN="$(pwd)/artifacts/round-stop-2026-05-09/files/fresh-publish/files.exe"
OUT="$(pwd)/artifacts/round-stop-2026-05-09/files/out"
DATA="$(pwd)/artifacts/round-stop-2026-05-09/files/data"
mkdir -p "$OUT" "$DATA"

# ─── Build the fixture tree (same shape as treex's, slight expansion for files) ───
FIX="$DATA/fixture"
rm -rf "$FIX" "$DATA/empty-dir" "$DATA/stress-1000" "$DATA/deep-50"
mkdir -p "$FIX/src" "$FIX/docs" "$FIX/.hidden-dir" "$FIX/ignored"
mkdir -p "$DATA/empty-dir" "$DATA/stress-1000"

# Hidden files
echo "hidden" > "$FIX/.hidden-file.txt"
echo "secret" > "$FIX/.hidden-dir/secret.txt"

# .gitignore content
cat > "$FIX/.gitignore" <<'EOF'
ignored/
*.log
EOF

# Gitignored
echo "ignored body" > "$FIX/ignored/ignored.txt"
echo "log content" > "$FIX/visible.log"

# Source files (text)
echo "// code 1" > "$FIX/src/code1.cs"
echo "// code 2" > "$FIX/src/code2.cs"
echo "# README" > "$FIX/src/README.md"

# Docs (text)
echo "# doc 1" > "$FIX/docs/doc1.md"
echo "# doc 2" > "$FIX/docs/doc2.md"

# Sized binaries (binary content via /dev/urandom)
head -c 1024     /dev/urandom > "$FIX/small-1k.bin"
head -c 102400   /dev/urandom > "$FIX/medium-100k.bin"
head -c 1048576  /dev/urandom > "$FIX/large-1M.bin"

# Unicode and special-char filenames (text)
echo "unicode" > "$FIX/unicode-中文.txt"
echo "emoji"   > "$FIX/emoji-🌏.txt"
echo "spaces"  > "$FIX/space file.txt"

# Stress: 1000 files in a single dir
for i in $(seq 1 1000); do
  echo "$i" > "$DATA/stress-1000/file-$(printf '%04d' $i).txt"
done

# Deep: 50-level nested directory
DEEP="$DATA/deep-50"
mkdir -p "$DEEP"
DEEP_PATH="$DEEP"
for i in $(seq 1 50); do
  DEEP_PATH="$DEEP_PATH/d$i"
  mkdir -p "$DEEP_PATH"
done
echo "deep leaf" > "$DEEP_PATH/leaf.txt"

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
run S01-help "--help" "$BIN --help"
run S02-version "--version" "$BIN --version"
run S03-describe "--describe valid JSON" "$BIN --describe"
run S04-happypath "list files in fixture" "$BIN \"$FIX\""
run S05-empty-dir "empty directory" "$BIN \"$DATA/empty-dir\""
run S06a-exit0 "exit 0 happy" "$BIN \"$FIX\""
run S06b-exit1-nonexistent "non-existent path → exit 1?" "$BIN \"$DATA/no-such-dir-12345\""
run S06c-exit125-bad-flag "bad flag → 125" "$BIN --not-a-flag"
run S07-ndjson "--ndjson valid JSON per line" "$BIN --ndjson \"$FIX\""
run S08a-stdout "default paths to stdout" "$BIN \"$FIX\" 2>/dev/null"
run S08b-json-stderr "--json summary on stderr" "$BIN --json \"$FIX\" 1>/dev/null"
run S09a-no-color "--no-color flag" "$BIN --no-color \"$FIX\""
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN \"$FIX\""
run S09c-color-force "--color force" "$BIN --color \"$FIX\""
run S10-pipe "piped output (no TTY)" "$BIN \"$FIX\" | head -3"
run S11-bad-flag "bad flag → 125" "$BIN --not-a-flag"
run S12-pathspace "path with space" "mkdir -p \"$DATA/dir with space\" && $BIN \"$DATA/dir with space\""

# ─── C13: depth (cf. treex F1) ───
run C13a-depth-0 "--max-depth 0 — root only? (treex was off-by-one)" "$BIN --max-depth 0 \"$FIX\""
run C13b-depth-1 "--max-depth 1 → +1 level" "$BIN --max-depth 1 \"$FIX\""
run C13c-depth-negative "--max-depth -1 → 125" "$BIN --max-depth -1 \"$FIX\""
run C13d-depth-non-numeric "--max-depth abc → 125" "$BIN --max-depth abc \"$FIX\""

# ─── C14: pattern filters ───
run C14a-glob-cs "--glob '*.cs'" "$BIN --glob '*.cs' \"$FIX\""
run C14b-ext-cs "--ext cs" "$BIN --ext cs \"$FIX\""
run C14c-regex-cs "--regex '\\.cs\$'" "$BIN --regex '\\.cs\$' \"$FIX\""
run C14d-ext-multiple "--ext cs --ext md" "$BIN --ext cs --ext md \"$FIX\""
run C14e-ignore-case "-i case-insensitive" "$BIN -i --glob '*.CS' \"$FIX\""

# ─── C15: size filters ───
run C15a-min-1k "--min-size 1k" "$BIN --min-size 1k \"$FIX\""
run C15b-max-100 "--max-size 100" "$BIN --max-size 100 \"$FIX\""
run C15c-min-1G "--min-size 1G" "$BIN --min-size 1G \"$FIX\""
run C15d-bad-suffix "--min-size 100kb (invalid) → 125" "$BIN --min-size 100kb \"$FIX\""
run C15e-impossible "--min-size 1M --max-size 1k → 0 results" "$BIN --min-size 1M --max-size 1k \"$FIX\""

# ─── C16: date filters ───
run C16a-newer-1h "--newer 1h" "$BIN --newer 1h \"$FIX\""
run C16b-older-1d "--older 1d" "$BIN --older 1d \"$FIX\""
run C16c-bad-duration "--newer 100x → 125" "$BIN --newer 100x \"$FIX\""

# ─── C17: type filter ───
run C17a-type-f "-t f files only" "$BIN -t f \"$FIX\""
run C17b-type-d "-t d directories" "$BIN -t d \"$FIX\""
run C17c-type-bogus "-t x → 125" "$BIN -t x \"$FIX\""

# ─── C18: text vs binary ───
run C18a-text "--text --type f" "$BIN --text --type f \"$FIX\""
run C18b-binary "--binary --type f" "$BIN --binary --type f \"$FIX\""
run C18c-text-binary "--text --binary mutually exclusive?" "$BIN --text --binary \"$FIX\""

# ─── C19: hidden / gitignore ───
run C19a-no-hidden "--no-hidden" "$BIN --no-hidden \"$FIX\""
run C19b-gitignore "--gitignore" "$BIN --gitignore \"$FIX\""

# ─── C20: output formats ───
run C20a-default "default path-per-line" "$BIN \"$FIX\""
run C20b-long "--long tab-delimited" "$BIN --long \"$FIX\""
run C20c-print0 "--print0 null-delimited" "$BIN --print0 \"$FIX\""
run C20d-absolute "--absolute paths" "$BIN --absolute \"$FIX\""
run C20e-ndjson-shape "--ndjson per-record fields (cf. treex F2)" "$BIN --ndjson \"$FIX\""
run C20f-ndjson-dir-size "--ndjson dir size_bytes (cf. treex F3)" "$BIN --ndjson -t d \"$FIX\""
run C20g-json-summary "--json summary" "$BIN --json \"$FIX\""

# ─── C21: stress ───
run C21a-1000-files "1000-file dir" "$BIN \"$DATA/stress-1000\" | wc -l"
run C21b-deep-50 "50-level deep tree" "$BIN \"$DATA/deep-50\" | wc -l"

# ─── C22: special chars ───
run C22a-unicode "Unicode filename matches" "$BIN \"$FIX\" | grep -c 中文"
run C22b-emoji "Emoji filename matches" "$BIN \"$FIX\" | grep -c 🌏"

# ─── C23: error paths (cf. treex F4) ───
run C23a-nonexistent "non-existent → exit 1" "$BIN \"$DATA/no-such-dir-12345\""
run C23b-file-not-dir "file (not dir) → exit/message? (treex F4)" "$BIN \"$FIX/src/code1.cs\""

# ─── C24: --describe parity ───
run C24a-describe-codes "describe enumerates 0/1/125" "$BIN --describe"

# ─── C25: documented examples ───
run C25a "files src --ext cs" "$BIN \"$FIX/src\" --ext cs"
run C25b "files . --text --type f" "$BIN \"$FIX\" --text --type f"
run C25c "files . --newer 1h --type f" "$BIN \"$FIX\" --newer 1h --type f"
run C25d "files . --long --ext cs" "$BIN \"$FIX\" --long --ext cs"
run C25e "files . --gitignore --no-hidden --ext cs" "$BIN \"$FIX\" --gitignore --no-hidden --ext cs"
run C25f "files . --glob '*.log' (pipe-able)" "$BIN \"$FIX\" --glob '*.log'"
run C25g "files . --ndjson | jq '.name'" "$BIN \"$FIX\" --ndjson | head -3"
run C25h "files . --ext cs --json" "$BIN \"$FIX\" --ext cs --json"
run C25i "files . --min-size 1k --max-size 10M" "$BIN \"$FIX\" --min-size 1k --max-size 10M"
run C25j "files . --older 7d --type f" "$BIN \"$FIX\" --older 7d --type f"
run C25k "files src --absolute --ext cs" "$BIN \"$FIX/src\" --absolute --ext cs"
run C25l "files . --glob '*.log' --print0" "$BIN \"$FIX\" --glob '*.log' --print0"

# ─── C26: pipe auto-detect ───
run C26a-pipe-no-ansi "piped output: no ANSI" "$BIN \"$FIX\""

# ─── C27: composability (suite-internal) ───
run C27a-print0-xargs "--print0 | xargs -0 wc -l" "$BIN \"$FIX\" --type f --print0 | xargs -0 wc -l"

# ─── W: Windows glob expansion (Windows-only; skipped on Linux/macOS) ───
IS_WINDOWS=false
case "$(uname -s)" in MINGW*|CYGWIN*|MSYS*) IS_WINDOWS=true ;; esac

if $IS_WINDOWS; then
  GLOB_DIR="$DATA/glob-fixture"
  rm -rf "$GLOB_DIR"
  mkdir -p "$GLOB_DIR"

  # W01: glob * expands — tool receives two dir paths and lists each, exit 0
  # files takes directories as roots; put two dirs under GLOB_DIR and use *.
  # Subshell cd prevents MSYS-style absolute path (/d/...) from reaching the tool; the tool
  # sees only the relative bare pattern * and expands it against its cwd.
  mkdir -p "$GLOB_DIR/one" "$GLOB_DIR/two"
  echo "aaa" > "$GLOB_DIR/one/f.txt"
  echo "bbb" > "$GLOB_DIR/two/g.txt"
  run W01-glob-expand "glob * expands to two dir roots (exit 0)" "( cd '$GLOB_DIR' && '$BIN' '*' )"

  # W02: ** → usage error, exit 125
  # Subshell-relative: avoids MSYS-path passthrough; tool sees literal ** from cwd.
  run W02-double-star "** rejected with usage error (exit 125)" "( cd '$GLOB_DIR' && '$BIN' '**' )"

  # W03: no-match pattern → literal passthrough → not-found, exit 1
  # Subshell-relative: avoids MSYS-path passthrough; tool sees literal *.nope from cwd.
  run W03-no-match "*.nope no-match → not-found (exit 1)" "( cd '$GLOB_DIR' && '$BIN' '*.nope' )"
else
  echo "=== W: Windows glob expansion — SKIPPED (not Windows) ==="
fi

echo
echo "=== Smoke run complete. Outputs in: $OUT ==="
ls "$OUT" | wc -l
echo "(4 files per smoke: cmd, stdout, stderr, exitcode)"
