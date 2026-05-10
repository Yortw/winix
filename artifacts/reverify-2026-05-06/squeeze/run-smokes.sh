#!/bin/bash
# Audit smoke suite for squeeze post-hotfix.
# Tests the hotfix at the scale where the user-found defect lived (10MB random truncated).
set +e
BIN="$(pwd)/artifacts/reverify-2026-05-06/squeeze/bin/squeeze.exe"
OUT="$(pwd)/artifacts/reverify-2026-05-06/squeeze/out"
DATA="$(pwd)/artifacts/reverify-2026-05-06/squeeze/data"
mkdir -p "$OUT" "$DATA"
mkdir -p "$DATA/dir with space"

# Set up payloads
echo -n "hello world" > "$DATA/hello.txt"
echo -n "" > "$DATA/empty.txt"
# 10MB incompressible random — the user-found defect class
head -c 10485760 /dev/urandom > "$DATA/random10mb.bin"
head -c 1024 /dev/urandom > "$DATA/random1kb.bin"

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
run S04-compress-file "compress hello.txt → hello.txt.gz" "$BIN \"$DATA/hello.txt\""
run S05-empty-stdin "compress empty stdin" "$BIN < \"$DATA/empty.txt\""
run S06a-exit0 "exit 0 success" "$BIN -d -c < \"$DATA/hello.txt.gz\""
run S06b-exit1-corrupt "exit 1 on corrupt input" "echo BOGUS | $BIN -d"
run S06c-exit2-bad-flag "exit 2 on bad arg" "$BIN --not-a-flag"
run S07-json "--json to stderr" "$BIN --json -c < \"$DATA/hello.txt\""
run S08a-stdout "stdout-only routing (compressed bytes to stdout)" "$BIN -c < \"$DATA/hello.txt\" 2>/dev/null"
run S08b-stderr "stderr only — JSON summary" "$BIN --json -c < \"$DATA/hello.txt\" 1>/dev/null"
run S09a-no-color "--no-color" "$BIN --no-color < \"$DATA/hello.txt\""
run S09b-no-color-env "NO_COLOR env" "NO_COLOR=1 $BIN < \"$DATA/hello.txt\""
run S09c-color "--color force" "$BIN --color < \"$DATA/hello.txt\""
run S10-pipe "stdin → stdout pipe-mode" "cat \"$DATA/hello.txt\" | $BIN | $BIN -d"
run S11-missing-arg "--brotli with --zstd → 2" "$BIN --brotli --zstd"
run S12-pathspace "compress file in path-with-space" "cp \"$DATA/hello.txt\" \"$DATA/dir with space/h.txt\"; $BIN \"$DATA/dir with space/h.txt\""

# squeeze-specific category (HOTFIX AUDIT)
run S13-1kb-roundtrip "1KB random round-trip" "$BIN \"$DATA/random1kb.bin\" -k -f && $BIN -d \"$DATA/random1kb.bin.gz\" -c -o \"$DATA/random1kb-decompressed.bin\""
run S14a-10mb-roundtrip "10MB random round-trip" "$BIN \"$DATA/random10mb.bin\" -k -f"
run S14b-10mb-decompress "10MB decompress check" "$BIN -d \"$DATA/random10mb.bin.gz\" -c -o \"$DATA/random10mb-decompressed.bin\""

# THE HOTFIX HEADLINE: 10MB truncated to various sizes — must REJECT (exit 1)
run S15a-10mb-truncated-50pct "10MB→5MB truncated REJECT (user-found defect)" "head -c 5242880 \"$DATA/random10mb.bin.gz\" > \"$DATA/trunc-50pct.gz\"; $BIN -d \"$DATA/trunc-50pct.gz\" -c"
run S15b-10mb-truncated-1mb "10MB→1MB truncated REJECT" "head -c 1048576 \"$DATA/random10mb.bin.gz\" > \"$DATA/trunc-1mb.gz\"; $BIN -d \"$DATA/trunc-1mb.gz\" -c"
run S15c-10mb-truncated-99pct "10MB→99% truncated REJECT" "SIZE=\$(wc -c < \"$DATA/random10mb.bin.gz\"); head -c \$((SIZE * 99 / 100)) \"$DATA/random10mb.bin.gz\" > \"$DATA/trunc-99pct.gz\"; $BIN -d \"$DATA/trunc-99pct.gz\" -c"

# Round-2 specific small truncations
run S16a-trunc-15b "15-byte truncation REJECT" "head -c 15 \"$DATA/random10mb.bin.gz\" > \"$DATA/trunc-15.gz\"; $BIN -d \"$DATA/trunc-15.gz\" -c"
run S16b-trunc-30b "30-byte truncation REJECT" "head -c 30 \"$DATA/random10mb.bin.gz\" > \"$DATA/trunc-30.gz\"; $BIN -d \"$DATA/trunc-30.gz\" -c"

# Multi-member rejection (current hotfix contract)
run S17a-multi-member "concatenated gzip REJECT" "cat \"$DATA/hello.txt.gz\" \"$DATA/hello.txt.gz\" > \"$DATA/double.gz\"; $BIN -d \"$DATA/double.gz\" -c"

# gzip(1) interop
run S18a-interop-write "squeeze writes, gzip(1) reads" "$BIN -c < \"$DATA/hello.txt\" > \"$DATA/from-squeeze.gz\"; gzip -dc \"$DATA/from-squeeze.gz\""
run S18b-interop-read "gzip(1) writes, squeeze reads" "gzip -c < \"$DATA/hello.txt\" > \"$DATA/from-gzip.gz\"; $BIN -d -c \"$DATA/from-gzip.gz\""

# FNAME-flagged input (gzip(1) embeds filename by default)
run S19-fname-flagged "FNAME-flag from gzip(1) decompresses" "rm -f \"$DATA/hello-fnamed.gz\"; gzip -k \"$DATA/hello.txt\" -c > \"$DATA/hello-fnamed.gz\"; $BIN -d -c \"$DATA/hello-fnamed.gz\""

# Other formats
run S20a-zstd-roundtrip "zstd round-trip 1KB random" "$BIN --zstd \"$DATA/random1kb.bin\" -k -f -o \"$DATA/r1kb.zst\"; $BIN -d -c \"$DATA/r1kb.zst\" > /dev/null"
run S20b-brotli-l11 "brotli level 11 small file" "$BIN --brotli --level 11 \"$DATA/hello.txt\" -k -f -o \"$DATA/h.br\"; $BIN -d -c \"$DATA/h.br\""

# Operational flags
run S21-force-overwrite "--force overwrite existing .gz" "$BIN \"$DATA/hello.txt\" -k -f"
run S22-keep-remove-warning "--keep --remove warning" "$BIN \"$DATA/hello.txt\" -k --remove -f"
run S23-output-empty "--output '' rejected" "$BIN -o '' \"$DATA/hello.txt\""

echo "=== Smoke run complete ==="
