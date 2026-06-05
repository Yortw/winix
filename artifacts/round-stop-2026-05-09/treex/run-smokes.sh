#!/bin/bash
# Category-aware smoke suite for treex.
# Run from repo root: bash artifacts/round-stop-2026-05-09/treex/run-smokes.sh
# Creates a controlled fixture tree under ./data; suite is deterministic against
# that fixture only — never tests against the real repo.

set +e
BIN="$(pwd)/artifacts/round-stop-2026-05-09/treex/fresh-publish/treex.exe"
OUT="$(pwd)/artifacts/round-stop-2026-05-09/treex/out"
DATA="$(pwd)/artifacts/round-stop-2026-05-09/treex/data"
mkdir -p "$OUT" "$DATA"

# ─── Build the fixture tree ───
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

# Source files
echo "// code 1" > "$FIX/src/code1.cs"
echo "// code 2" > "$FIX/src/code2.cs"
echo "# README" > "$FIX/src/README.md"

# Docs
echo "# doc 1" > "$FIX/docs/doc1.md"
echo "# doc 2" > "$FIX/docs/doc2.md"

# Sized binaries
head -c 1024     /dev/urandom > "$FIX/small-1k.bin"
head -c 102400   /dev/urandom > "$FIX/medium-100k.bin"
head -c 1048576  /dev/urandom > "$FIX/large-1M.bin"

# Unicode and special-char filenames
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
run S04-happypath "tree of fixture root" "$BIN \"$FIX\""
run S05-empty-dir "empty directory" "$BIN \"$DATA/empty-dir\""
run S06a-exit0 "exit 0 happy" "$BIN \"$FIX\""
run S06b-exit1-nonexistent "non-existent path → exit 1?" "$BIN \"$DATA/no-such-dir-12345\""
run S06c-exit125-bad-flag "bad flag → 125" "$BIN --not-a-flag"
run S07-ndjson "--ndjson valid JSON per line" "$BIN --ndjson \"$FIX\""
run S08a-stdout-tree "default tree on stdout" "$BIN \"$FIX\" 2>/dev/null"
run S08b-json-stderr "--json summary on stderr" "$BIN --json \"$FIX\" 1>/dev/null"
run S09a-no-color "--no-color flag" "$BIN --no-color \"$FIX\""
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN \"$FIX\""
run S09c-color-force "--color force" "$BIN --color \"$FIX\""
run S10-pipe-no-ansi "piped output → no ANSI codes" "$BIN \"$FIX\" | head -3"
run S11-bad-flag "bad flag → 125" "$BIN --not-a-flag"
run S12-pathspace "path with space (positional)" "mkdir -p \"$DATA/dir with space\" && $BIN \"$DATA/dir with space\""

# ─── C13: depth ───
run C13a-depth-0 "--max-depth 0 → root only" "$BIN --max-depth 0 \"$FIX\""
run C13b-depth-1 "--max-depth 1 → root + immediate children" "$BIN --max-depth 1 \"$FIX\""
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
run C15b-max-100 "--max-size 100 (bare bytes)" "$BIN --max-size 100 \"$FIX\""
run C15c-min-1G "--min-size 1G (large unit)" "$BIN --min-size 1G \"$FIX\""
run C15d-bad-suffix "--min-size 100kb (invalid suffix) → 125" "$BIN --min-size 100kb \"$FIX\""
run C15e-impossible-range "--min-size 1M --max-size 1k impossible → 0 results" "$BIN --min-size 1M --max-size 1k \"$FIX\""

# ─── C16: date filters ───
run C16a-newer-1h "--newer 1h" "$BIN --newer 1h \"$FIX\""
run C16b-older-1d "--older 1d" "$BIN --older 1d \"$FIX\""
run C16c-bad-duration-unit "--newer 100x → 125" "$BIN --newer 100x \"$FIX\""

# ─── C17: type filter ───
run C17a-type-f "-t f files only" "$BIN -t f \"$FIX\""
run C17b-type-d "-t d directories only" "$BIN -t d \"$FIX\""
run C17c-type-bogus "-t x → 125" "$BIN -t x \"$FIX\""

# ─── C18: hidden / gitignore ───
run C18a-no-hidden "--no-hidden skips dotfiles" "$BIN --no-hidden \"$FIX\""
run C18b-gitignore "--gitignore respects .gitignore" "$BIN --gitignore \"$FIX\""
run C18c-both "--no-hidden --gitignore" "$BIN --no-hidden --gitignore \"$FIX\""

# ─── C19: output formats ───
run C19a-ndjson-shape "--ndjson valid JSON each line" "$BIN --ndjson \"$FIX\""
run C19b-ndjson-count "--ndjson record count" "$BIN --ndjson \"$FIX\" | wc -l"
run C19c-json-summary "--json summary" "$BIN --json \"$FIX\""

# ─── C20: sort ───
run C20a-sort-name "--sort name (default)" "$BIN --sort name \"$FIX\""
run C20b-sort-size "--sort size" "$BIN --sort size --size \"$FIX\""
run C20c-sort-modified "--sort modified" "$BIN --sort modified \"$FIX\""
run C20d-sort-bogus "--sort bogus → 125" "$BIN --sort bogus \"$FIX\""

# ─── C21: stress ───
run C21a-1000-files "1000-file dir completes" "$BIN \"$DATA/stress-1000\" | wc -l"
run C21b-deep-50 "50-level deep tree completes" "$BIN \"$DATA/deep-50\" | wc -l"

# ─── C22: special chars ───
run C22a-unicode "Unicode filename in tree output" "$BIN \"$FIX\" | grep -c 中文"
run C22b-emoji "Emoji filename in tree output" "$BIN \"$FIX\" | grep -c 🌏"

# ─── C23: error paths ───
run C23a-nonexistent "non-existent path → exit 1?" "$BIN \"$DATA/no-such-dir-12345\""
run C23b-file-not-dir "treex on a file (not dir) → ???" "$BIN \"$FIX/src/code1.cs\""

# ─── C24: --describe parity ───
run C24a-describe-codes "describe enumerates 0/1/125" "$BIN --describe"

# ─── C25: documented examples ───
run C25a-readme-ex1 "treex (default cwd)" "$BIN \"$FIX\""
run C25b-readme-ex2 "treex --ext cs" "$BIN \"$FIX\" --ext cs"
run C25c-readme-ex3 "treex --size --gitignore --no-hidden" "$BIN --size --gitignore --no-hidden \"$FIX\""
run C25d-readme-ex4 "treex --size --sort size" "$BIN --size --sort size \"$FIX\""
run C25e-readme-ex5 "treex -d 2" "$BIN -d 2 \"$FIX\""
run C25f-readme-ex6 "treex multiple roots" "$BIN \"$FIX/src\" \"$FIX/docs\""
run C25g-readme-ex7 "treex --ndjson | jq '.name'" "$BIN --ndjson \"$FIX\" | head -3"

# ─── C26: pipe auto-detect ───
run C26a-pipe-no-ansi "piped output: no ANSI escape sequences" "$BIN \"$FIX\""

# ─── C27: dirs-only ───
run C27a-dirs-only "-D shows only directories" "$BIN -D \"$FIX\""

# ─── C28: --size rollup ───
run C28a-size-rollup "--size includes rollups" "$BIN --size \"$FIX\""

# ─── W: Windows glob expansion (Windows-only; skipped on Linux/macOS) ───
IS_WINDOWS=false
case "$(uname -s)" in MINGW*|CYGWIN*|MSYS*) IS_WINDOWS=true ;; esac

if $IS_WINDOWS; then
  GLOB_DIR="$DATA/glob-fixture"
  rm -rf "$GLOB_DIR"
  mkdir -p "$GLOB_DIR/one" "$GLOB_DIR/two"
  echo "aaa" > "$GLOB_DIR/one/f.txt"
  echo "bbb" > "$GLOB_DIR/two/g.txt"

  # W01: glob * expands — treex receives two dir roots and trees each, exit 0
  # Subshell cd prevents MSYS-style absolute path (/d/...) from reaching the tool; the tool
  # sees only the relative bare pattern * and expands it against its cwd.
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
