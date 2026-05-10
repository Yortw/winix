#!/bin/bash
# Tier-1 smoke suite for envvault — uses --flag style action selectors.
set +e
BIN="d:/projects/winix/artifacts/round-stop-2026-05-09/envvault/fresh-publish/envvault.exe"
OUT="d:/projects/winix/artifacts/round-stop-2026-05-09/envvault/out"
mkdir -p "$OUT"

NS="winix-smoke-$$"

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
run B05 "unknown flag" "$BIN" --invalid-flag
run B06 "--list (no namespace, list all)" "$BIN" --list
run B07 "--get nonexistent namespace+key" "$BIN" --get "$NS" no-such-key
run B08 "--unset nonexistent" "$BIN" --unset "$NS" no-such-key
run B09 "--list nonexistent namespace" "$BIN" --list "$NS"
run B10 "--no-color --list" "$BIN" --no-color --list
run B11 "NO_COLOR env --list" env NO_COLOR=1 "$BIN" --list
run B12 "--set with --value (round-trip prep)" "$BIN" --set "$NS" k1 --value v1

# ── Envvault-specific extensions ──
run E01 "--get round-trip after --set" "$BIN" --get "$NS" k1
run E02 "--list shows the key" "$BIN" --list "$NS"
run E03 "--unset the key" "$BIN" --unset "$NS" k1
run E04 "--list now empty" "$BIN" --list "$NS"
run E05 "--get after --unset (should be missing)" "$BIN" --get "$NS" k1
run E06 "--set with empty --value (refuses by default)" "$BIN" --set "$NS" empty-key --value ""

echo
echo "==== envvault done: $(ls $OUT | wc -l) result files ===="
