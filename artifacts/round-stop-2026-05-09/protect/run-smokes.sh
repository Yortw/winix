#!/bin/bash
# Tier-1 smoke suite for protect (file encrypt) — round-trip with unprotect.
set +e
P_BIN="d:/projects/winix/artifacts/round-stop-2026-05-09/protect/fresh-publish/protect.exe"
U_BIN="d:/projects/winix/artifacts/round-stop-2026-05-09/unprotect/fresh-publish/unprotect.exe"
OUT="d:/projects/winix/artifacts/round-stop-2026-05-09/protect/out"
DATA="d:/projects/winix/artifacts/round-stop-2026-05-09/protect/data"
mkdir -p "$OUT" "$DATA"

# Test fixtures
echo "Hello protected world" > "$DATA/plain.txt"
head -c 1024 /dev/urandom > "$DATA/binary.bin"

run() {
  local id="$1"; local desc="$2"; shift 2
  echo "=== $id: $desc ==="
  echo "$@" > "$OUT/$id.cmd"
  timeout 10s "$@" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo "$?" > "$OUT/$id.exit"
}

# ── Baseline 12 (protect) ──
run B01-protect "protect --version" "$P_BIN" --version
run B02-protect "protect --help" "$P_BIN" --help
run B03-protect "protect --describe" "$P_BIN" --describe
run B04-protect "protect no args" "$P_BIN"
run B05-protect "protect unknown flag" "$P_BIN" --invalid-flag
run B06-protect "protect missing input file" "$P_BIN" "$DATA/no-such-file.txt"

# ── Baseline 12 (unprotect) ──
run B01-unprotect "unprotect --version" "$U_BIN" --version
run B02-unprotect "unprotect --help" "$U_BIN" --help
run B03-unprotect "unprotect --describe" "$U_BIN" --describe
run B04-unprotect "unprotect no args" "$U_BIN"
run B05-unprotect "unprotect unknown flag" "$U_BIN" --invalid-flag
run B06-unprotect "unprotect missing input" "$U_BIN" "$DATA/no-such.enc"

# ── Round-trip extensions ──
run R01 "protect text file -> .prot" "$P_BIN" "$DATA/plain.txt"
run R02pre "remove plaintext before unprotect (to avoid destination-exists)" rm "$DATA/plain.txt"
run R02 "unprotect .prot -> recreates plain.txt" "$U_BIN" "$DATA/plain.txt.prot"
run R03 "protect binary file" "$P_BIN" "$DATA/binary.bin"
run R04pre "remove binary before unprotect" rm "$DATA/binary.bin"
run R04 "unprotect binary -> recreates binary.bin" "$U_BIN" "$DATA/binary.bin.prot"
run R05 "protect existing .prot (refuses without --force)" "$P_BIN" "$DATA/plain.txt"
run R06 "unprotect non-protected file (corrupt header)" "$U_BIN" "$DATA/plain.txt"

echo
echo "==== protect/unprotect done: $(ls $OUT | wc -l) result files ===="
