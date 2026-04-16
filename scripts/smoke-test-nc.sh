#!/usr/bin/env bash
# smoke-test-nc.sh — End-to-end smoke tests for the nc tool.
#
# Runs a mix of loopback tests (fast, offline) and live-endpoint tests
# (require internet). Intended to verify a freshly built binary before a
# release; not a replacement for the xUnit test suite.
#
# Usage:
#   ./scripts/smoke-test-nc.sh                   # uses default built Release AOT binary
#   ./scripts/smoke-test-nc.sh /path/to/nc.exe   # test a specific binary
#
# Exit code: 0 if all tests pass, 1 otherwise.

set -uo pipefail   # NOT -e — we want to tally failures and keep running

# --- Resolve binary ---
if [ $# -ge 1 ]; then
    NC="$1"
else
    NC="src/nc/bin/Release/net10.0/win-x64/publish/nc.exe"
fi

if [ ! -x "$NC" ] && [ ! -f "$NC" ]; then
    echo "ERROR: nc binary not found at '$NC'" >&2
    echo "Build it first: dotnet publish src/nc/nc.csproj -c Release -r win-x64" >&2
    exit 1
fi

echo "Testing: $NC"
echo "Version: $("$NC" --version)"
echo

# --- Test tracking ---
PASS=0
FAIL=0
FAILURES=()

assert_exit() {
    local label="$1"
    local expected="$2"
    local actual="$3"
    if [ "$actual" = "$expected" ]; then
        printf "  [PASS] %s\n" "$label"
        PASS=$((PASS + 1))
    else
        printf "  [FAIL] %s (expected exit %s, got %s)\n" "$label" "$expected" "$actual"
        FAIL=$((FAIL + 1))
        FAILURES+=("$label")
    fi
}

assert_contains() {
    local label="$1"
    local needle="$2"
    local haystack="$3"
    # Use bash string search instead of piping to grep — avoids issues with
    # large haystacks being truncated or re-encoded by echo/printf on Windows.
    if [[ "$haystack" == *"$needle"* ]]; then
        printf "  [PASS] %s\n" "$label"
        PASS=$((PASS + 1))
    else
        printf "  [FAIL] %s (output missing '%s')\n" "$label" "$needle"
        FAIL=$((FAIL + 1))
        FAILURES+=("$label")
    fi
}

# --- Section 1: self-check ---
echo "=== Self-check ==="

"$NC" --version >/dev/null
assert_exit "--version exits 0" 0 $?

"$NC" --help >/dev/null
assert_exit "--help exits 0" 0 $?

DESCRIBE_OUT=$("$NC" --describe)
assert_exit "--describe exits 0" 0 $?
assert_contains "--describe contains tool name" '"tool":"nc"' "$DESCRIBE_OUT"
assert_contains "--describe lists --check flag" '"long":"--check"' "$DESCRIBE_OUT"

echo

# --- Section 2: loopback port checks ---
echo "=== Loopback port checks ==="

# Claim-and-release an ephemeral port to get a known-closed one.
# Python is available on most Windows dev machines; fall back to something
# that's likely present if it isn't.
CLOSED_PORT=$(python -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()' 2>/dev/null || echo "59999")
echo "  (using port $CLOSED_PORT as known-closed)"

# Start a background listener for the known-open port
# Use a different nc instance — the tool we're testing.
"$NC" --listen 17777 >/dev/null 2>&1 &
LISTENER_PID=$!
trap 'kill $LISTENER_PID 2>/dev/null || true' EXIT

# Give the listener a moment to bind.
sleep 0.5

# Closed port — silent by default, exit 1.
"$NC" -z 127.0.0.1 "$CLOSED_PORT" --timeout 3
assert_exit "closed port returns exit 1" 1 $?

# Open port — exit 0, stdout has "17777 open"
OUT=$("$NC" -z 127.0.0.1 17777 --timeout 3)
RC=$?
assert_exit "open port returns exit 0" 0 $RC
assert_contains "open port printed to stdout" "17777 open" "$OUT"

# Re-spawn listener because the previous -z 17777 closed the pending accept
kill $LISTENER_PID 2>/dev/null || true
wait $LISTENER_PID 2>/dev/null || true
"$NC" --listen 17777 >/dev/null 2>&1 &
LISTENER_PID=$!
sleep 0.5

# Multi-port check with one open, one closed — exit 1 ("some_closed")
OUT=$("$NC" -z 127.0.0.1 "17777,$CLOSED_PORT" --timeout 3 --verbose 2>&1)
RC=$?
assert_exit "mixed open+closed returns exit 1" 1 $RC
assert_contains "mixed check prints open port" "17777 open" "$OUT"
assert_contains "mixed check prints closed port under --verbose" "closed" "$OUT"

# JSON output — respawn listener again because mixed check consumed it
kill $LISTENER_PID 2>/dev/null || true
wait $LISTENER_PID 2>/dev/null || true
"$NC" --listen 17777 >/dev/null 2>&1 &
LISTENER_PID=$!
sleep 0.5

JSON_OUT=$("$NC" -z 127.0.0.1 17777 --json --timeout 3 2>&1)
assert_contains "JSON contains tool field" '"tool":"nc"' "$JSON_OUT"
assert_contains "JSON contains status=open" '"status":"open"' "$JSON_OUT"
assert_contains "JSON contains exit_reason" '"exit_reason":"all_open"' "$JSON_OUT"

echo

# --- Section 3: TCP client roundtrip (loopback) ---
echo "=== TCP client roundtrip (loopback) ==="

# Start a "mini echo server" using PowerShell — listens once, echoes what it reads, closes.
# This is the reverse test: nc is the client.
powershell.exe -NoProfile -Command "
\$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 17778)
\$listener.Start()
\$client = \$listener.AcceptTcpClient()
\$stream = \$client.GetStream()
\$buf = New-Object byte[] 4096
\$read = \$stream.Read(\$buf, 0, \$buf.Length)
\$stream.Write(\$buf, 0, \$read)
\$stream.Close()
\$client.Close()
\$listener.Stop()
" >/dev/null 2>&1 &
ECHO_PID=$!
sleep 0.8

ECHO_OUT=$(echo "ping" | "$NC" 127.0.0.1 17778 --timeout 3)
RC=$?
wait $ECHO_PID 2>/dev/null || true

assert_exit "TCP roundtrip exits 0" 0 $RC
assert_contains "TCP roundtrip echoes input" "ping" "$ECHO_OUT"

echo

# --- Section 4: usage errors ---
echo "=== Usage errors ==="

"$NC" --listen --check 127.0.0.1 17777 2>/dev/null
assert_exit "mode conflict returns exit 125" 125 $?

"$NC" --tls --udp example.com 443 2>/dev/null
assert_exit "tls+udp returns exit 125" 125 $?

"$NC" --tls --listen 8443 2>/dev/null
assert_exit "tls+listen returns exit 125" 125 $?

"$NC" -4 -6 example.com 80 2>/dev/null
assert_exit "ipv4+ipv6 returns exit 125" 125 $?

echo

# --- Section 5: error handling ---
echo "=== Error handling ==="

"$NC" -z nonexistent-host-abc123xyz.invalid 80 --timeout 3
assert_exit "DNS failure returns exit 1" 1 $?

"$NC" -z 127.0.0.1 "$CLOSED_PORT" --timeout 3
assert_exit "refused connection returns exit 1" 1 $?

echo

# --- Section 6: live-endpoint tests (skip with SKIP_LIVE=1) ---
if [ "${SKIP_LIVE:-0}" != "1" ]; then
    echo "=== Live-endpoint tests (set SKIP_LIVE=1 to skip) ==="

    "$NC" -z google.com 443 --timeout 5 >/dev/null
    assert_exit "google.com:443 open" 0 $?

    "$NC" -z 1.1.1.1 53 --timeout 5 >/dev/null
    assert_exit "1.1.1.1:53 open" 0 $?

    # HTTPS smoke test via --tls.
    # Uses www.google.com because Cloudflare-fronted hosts (example.com,
    # httpbin.org) silently drop minimal HTTP/1.0 requests without a
    # User-Agent — Google is more lenient.
    # Uses printf in the pipeline to avoid echo's trailing newline
    # corrupting the CRLF-terminated HTTP request.
    RESP=$(printf 'GET / HTTP/1.0\r\nHost: www.google.com\r\nUser-Agent: winix-nc-smoketest\r\nConnection: close\r\n\r\n' \
        | "$NC" --tls www.google.com 443 --timeout 15 2>/dev/null)
    RC=$?
    assert_exit "TLS client exits 0" 0 $RC
    echo "  (TLS response length=${#RESP} bytes, first line: $(echo "$RESP" | head -1))"
    assert_contains "TLS response starts with HTTP" "HTTP/1." "$RESP"
    assert_contains "TLS response has 200 OK" "200" "$RESP"

    echo
fi

# --- Summary ---
echo "=== Summary ==="
TOTAL=$((PASS + FAIL))
echo "Passed: $PASS / $TOTAL"
if [ "$FAIL" -gt 0 ]; then
    echo "Failed: $FAIL"
    for f in "${FAILURES[@]}"; do
        echo "  - $f"
    done
    exit 1
fi
echo "All tests passed."
exit 0
