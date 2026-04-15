# nc

Cross-platform netcat replacement — TCP/UDP send/receive, port checks, TLS clients.

Built for the developer/sysadmin on Windows who's tired of typing `Test-NetConnection`. Familiar `nc` muscle memory works (`-z`, `-l`, `-u`, `-w`, `-4`, `-6`), but long-form flags are first-class.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/nc
```

### Winget (Windows, stable releases)

```bash
winget install Winix.NetCat
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.NetCat
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
nc [options] HOST PORT          # connect (default)
nc --listen [options] PORT      # listen for one connection
nc --check [options] HOST PORT  # check port(s)
```

### Examples

```bash
# Quick port check
nc -z target.com 443

# Multiple ports
nc -z target.com 80,443,5432

# Range check with JSON
nc -z target.com 1-1024 --json

# TCP send/receive
echo "GET / HTTP/1.0" | nc target.com 80

# TLS client
nc --tls api.example.com 443

# UDP send
nc -u dnsserver 53 < query.bin

# Listen for one connection
nc -l 8080

# File transfer
nc target.com 80 < request.bin > response.bin
```

## Options

| Long | Short | Description |
|------|-------|-------------|
| `--listen` | `-l` | Listen for one inbound connection |
| `--check` | `-z` | Check whether port(s) are open |
| `--udp` | `-u` | Use UDP (default is TCP) |
| `--tls` | (also `--ssl`) | Wrap TCP connection in TLS (client only) |
| `--insecure` | | Skip TLS certificate validation |
| `--ipv4` | `-4` | Force IPv4 |
| `--ipv6` | `-6` | Force IPv6 |
| `--timeout SEC` | `-w SEC` | Connect/idle timeout |
| `--bind ADDR` | | Listener bind interface |
| `--no-shutdown` | | Don't half-close on stdin EOF |
| `--verbose` | `-v` | Show closed/timeout ports too in check mode |
| `--json` | | Emit JSON summary to stderr |
| `--describe` | | Agent metadata as JSON |
| `--color` / `--no-color` | | Colour control |
| `--help` / `--version` | | Standard |

## Port Range Syntax

The `--check` mode accepts single ports, ranges, lists, or mixed specifiers:

```bash
nc -z host 80                       # single
nc -z host 80-1000                  # range (inclusive)
nc -z host 80,443,8080              # list
nc -z host 80-100,443,8080-8090     # mixed
```

Stdout prints one line per **open** port (suitable for piping). Closed/timeout/error ports go to stderr only under `--verbose`.

## Half-close on stdin EOF

When stdin reaches EOF (e.g. `echo request | nc host 80` or `nc host 80 < file.bin`), `nc` calls `Socket.Shutdown(Send)` so the peer sees end-of-stream on its read side. Without this, request/response protocols like HTTP hang because the server waits for more request bytes before sending a response.

Use `--no-shutdown` only for the rare line-oriented protocols that treat half-close as a full connection close.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Connection refused / unreachable / closed port / TLS failure |
| 2 | Timeout |
| 125 | Usage error |
| 126 | Permission denied (privileged port) |
| 130 | Interrupted (Ctrl-C) |

## Colour

- Status messages on stderr use colour: green = open, red = closed/error, yellow = timeout/warning.
- Bytes relayed on stdout are never coloured.
- Respects `NO_COLOR` and `--color` / `--no-color`.

## Part of Winix

`nc` is part of the [Winix](../../README.md) CLI toolkit.
