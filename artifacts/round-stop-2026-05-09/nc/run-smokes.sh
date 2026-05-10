#!/bin/bash
# Tier-1 smoke suite for nc (netcat) — baseline-12 + nc-specific extensions.
# Skips long-running listener probes that need real socket teardown.
set +e
BIN="d:/projects/winix/artifacts/round-stop-2026-05-09/nc/fresh-publish/nc.exe"
OUT="d:/projects/winix/artifacts/round-stop-2026-05-09/nc/out"
mkdir -p "$OUT"

run() {
  local id="$1"; local desc="$2"; shift 2
  echo "=== $id: $desc ==="
  echo "$@" > "$OUT/$id.cmd"
  timeout 5s "$@" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo "$?" > "$OUT/$id.exit"
}

# ── Baseline 12 ──
run B01 "--version" "$BIN" --version
run B02 "--help" "$BIN" --help
run B03 "--describe" "$BIN" --describe
run B04 "no args" "$BIN"
run B05 "unknown flag" "$BIN" --invalid-flag
run B06 "single positional (port-only is invalid for client)" "$BIN" 80
run B07 "bad host (DNS)" "$BIN" definitely-not-a-real-host-xyz-12345.example 80
run B08 "bad port (non-numeric)" "$BIN" example.com not-a-port
run B09 "bad port (out of range)" "$BIN" example.com 99999
run B10 "negative port" "$BIN" example.com -1
run B11 "--listen with no port" "$BIN" --listen
run B12 "--no-color env" env NO_COLOR=1 "$BIN" --version

# ── NC-specific extensions ──
run N01 "--listen + invalid port" "$BIN" --listen bogus
run N02 "--listen with valid free port (timeout-bound)" "$BIN" --listen 0
run N03 "--udp client to bogus host" "$BIN" --udp definitely-not-a-host.example 80

echo
echo "==== nc done: $(ls $OUT | wc -l) result files ===="
