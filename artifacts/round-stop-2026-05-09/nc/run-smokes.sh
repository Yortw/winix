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

# ── Capability-surface addition (2026-06-06) ──
# NX1: SIGINT during listen-accept — the interrupted contract end-to-end.
# EXPECTED RESULT: exit file = 124 AND stderr = the line `nc: interrupted` followed by the
# envelope {"...,"exit_code":130,"exit_reason":"interrupted","error":"user cancelled"}.
# (nc's cancel stderr is text-line THEN envelope — unlike wargs's envelope-only; probed
# 2026-06-06 on the linux-x64 binary, exit ~30ms after the INT, nothing lingers.)
# 124 is GNU timeout's OWN code (it reports 124 whenever it had to send the signal).
# Inner 2s INT nests under run()'s 5s outer timeout with ~3s headroom. Linux-only:
# MSYS cannot deliver SIGINT to native exes; covered on Windows by the seam
# cancellation tests (CliRunAsyncUnlockedTests).
if [ "$(uname -s)" = "Linux" ]; then
  run NX1 "SIGINT during listen-accept -> interrupted envelope (exit 124 = timeout's own code)" timeout -s INT 2 "$BIN" --json -l 18097
else
  echo "=== NX1: SKIPPED (Windows: no SIGINT delivery to native exe from this harness) ==="
  echo "skipped" > "$OUT/NX1.exit"
fi

echo
echo "==== nc done: $(ls $OUT | wc -l) result files ===="
