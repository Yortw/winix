# nc — AI Agent Guide

## What This Tool Does

`nc` is a cross-platform netcat replacement for testing and interacting with TCP/UDP services. It can probe ports for openness (check mode), connect outbound and pipe bytes between stdin/stdout and the remote (connect mode), or accept one inbound connection and relay bytes (listen mode). It supports TLS for client connections, IPv4/IPv6 selection, timeouts, and a modern half-close behaviour so HTTP-style request/response patterns work without hanging.

## Platform Story

Cross-platform. On **Windows**, `nc` replaces the verbose `Test-NetConnection` cmdlet and fills the gap left by no native `nc` binary. On **Linux/macOS**, `nc` competes with the BSD/GNU/ncat forks — each with subtly different flag sets. Winix `nc` is consistent across all platforms, ships as a single native AOT binary, and keeps classic short flags (`-z`, `-l`, `-u`, `-w`, `-4`, `-6`) working as aliases for descriptive long flags.

## When to Use This

- **Port is open / service reachable** — `nc -z target.com 443` exits 0 if open, 1 if closed, 2 on timeout.
- **Quick TCP send-and-receive** — `echo "GET / HTTP/1.0" | nc target.com 80` prints the HTTP response to stdout.
- **HTTPS smoke test** — `nc --tls api.example.com 443` authenticates TLS then relays bytes.
- **UDP send** — `nc -u dnsserver 53 < query.bin` sends a datagram and optionally waits briefly for a reply.
- **Listen for one connection** — `nc -l 8080` accepts one TCP connection, relays it with stdin/stdout, exits.
- **File transfer over TCP** — `nc target.com 80 < request.bin > response.bin` works for free via stdin/stdout piping.
- **Scan a small port range on one host** — `nc -z target 80-1024` (single host only; not an nmap replacement).

## Common Patterns

**Quick port check:**
```bash
nc -z target.com 443
```

**Multiple ports in one call:**
```bash
nc -z target.com 80,443,5432
```

**Port range:**
```bash
nc -z target.com 1-1024
```

**Mixed syntax:**
```bash
nc -z target.com 80-100,443,8080-8090
```

**TCP client:**
```bash
echo "HELO example.com" | nc smtp.example.com 25
```

**TLS client (skip cert validation for self-signed):**
```bash
nc --tls --insecure dev-server.local 8443
```

**UDP client with response wait:**
```bash
echo "test" | nc -u syslog.local 514 --timeout 2
```

**Listen on localhost only:**
```bash
nc --listen --bind 127.0.0.1 8080
```

**Force IPv6:**
```bash
nc -6 ipv6.example.com 80
```

**JSON summary to stderr:**
```bash
nc -z target.com 1-1024 --json
```

**Structured agent metadata:**
```bash
nc --describe
```

## Composing with Other Tools

**nc + xargs** — process open-port output:
```bash
nc -z target.com 22,80,443 | xargs -I{} echo "open: {}"
```

**nc + wargs** — run a command per open port:
```bash
nc -z target.com 1-1024 | wargs curl -sI http://target.com:{}
```

**nc + jq** — extract fields from the JSON summary:
```bash
nc -z target.com 80,443 --json 2>&1 >/dev/null | jq '.ports[] | select(.status=="open") | .port'
```

**nc + timeit** — measure port-open wall time:
```bash
timeit -- nc -z target.com 443
```

**nc + tee** — capture both response and status:
```bash
nc target.com 80 < request.bin | tee response.bin
```

## Mode Selection

`nc` operates in one of three mutually-exclusive modes:

1. **Connect (default)** — `nc HOST PORT`. Opens an outbound connection, relays stdin/stdout, exits when both directions finish.
2. **Listen** — `nc --listen PORT` (or `nc -l PORT`). Binds a port, accepts one connection, relays, exits.
3. **Check** — `nc --check HOST PORT-SPEC` (or `nc -z HOST PORT-SPEC`). Probes ports for openness and exits without exchanging data.

Combining mode flags produces a usage error (exit 125).

## Output Streams

- **stdout** receives bytes sent by the remote peer (connect/listen modes), or one line per open port (check mode). Bytes are never coloured to keep pipelines binary-safe.
- **stderr** receives all human-facing status, errors, warnings, and (when `--json` is used) the machine-readable summary. In check mode under `--verbose`, closed and timeout ports are reported here as well.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success — connection completed, all probed ports open, listener served one exchange |
| 1 | Connection refused, host unreachable, DNS failure, bind failure, any closed port in check mode, TLS handshake failure |
| 2 | Timeout |
| 125 | Usage error |
| 126 | Permission denied (e.g. bind to a privileged port without elevation) |
| 130 | Interrupted by Ctrl-C |

Check mode reports the **worst** outcome across all probed ports (timeout > closed > open).

## JSON Summary (`--json`)

Emitted to stderr after the run.

**Check mode:**
```json
{
  "tool": "nc",
  "version": "0.2.0",
  "mode": "check",
  "host": "target.com",
  "ports": [
    { "port": 80, "status": "open", "latency_ms": 23.50 },
    { "port": 443, "status": "open", "latency_ms": 24.10 },
    { "port": 5432, "status": "closed" }
  ],
  "exit_code": 1,
  "exit_reason": "some_closed"
}
```

**Connect/Listen mode:**
```json
{
  "tool": "nc",
  "version": "0.2.0",
  "mode": "connect",
  "host": "target.com",
  "port": 80,
  "protocol": "tcp",
  "tls": false,
  "remote_address": "93.184.216.34:80",
  "bytes_sent": 42,
  "bytes_received": 1305,
  "duration_ms": 187.00,
  "exit_code": 0,
  "exit_reason": "success"
}
```

## Gotchas

**Half-close on stdin EOF is on by default.** When stdin ends, `nc` calls `Socket.Shutdown(Send)` so the peer sees EOF on its read side and can respond. Without this, HTTP-style request/response protocols hang. A small number of line-oriented protocols treat this as a full disconnect; use `--no-shutdown` for those.

**Check mode is bounded by timeout, not by port count.** Each port probe runs up to `--timeout` seconds (default 10). Up to 32 probes run concurrently. A scan of 1024 ports against a cold host can therefore take ~320 seconds in the worst case — but open and refused ports return quickly.

**TLS listen mode is not supported in v0.2.0.** `--tls --listen` is rejected with a usage error. Server-side TLS requires certificate management that is deferred to a later release.

**Privileged ports need elevation on Unix (and sometimes Windows).** Binding ports below 1024 fails with exit 126 if the process lacks the required rights. Elevate the shell or choose a higher port.

**Windows loopback `ConnectionRefused` is slow.** On Windows, `TcpClient.ConnectAsync` to a closed loopback port can take 1-2 seconds before returning `ConnectionRefused` due to SYN retransmission behaviour. This affects `-z` probes against closed ports on the local machine but is harmless — the probe still returns the correct result.

**UDP "connection" is stateless.** Unlike TCP, UDP send doesn't verify the peer is listening. A successful `nc -u host port < data` only confirms the kernel accepted the datagram for transmission; silence from the peer is indistinguishable from a black hole.

**Port scanning etiquette.** `nc -z` against hosts you don't own or manage may violate acceptable-use policies or local laws. Use against your own infrastructure or with explicit permission.
