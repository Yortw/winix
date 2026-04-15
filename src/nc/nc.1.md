% NC(1) Winix | User Commands
% Troy Willmot
% 2026-04-16

# NAME

nc - cross-platform netcat replacement for TCP/UDP send/receive, port checks, and TLS clients

# SYNOPSIS

**nc** [*options*] *host* *port*

**nc** **--listen** [*options*] *port*

**nc** **--check** [*options*] *host* *port-spec*

# DESCRIPTION

**nc** opens a TCP or UDP connection, listens for one inbound connection, or probes one or more ports for openness. Familiar **nc** short flags (**-z**, **-l**, **-u**, **-w**, **-4**, **-6**) are accepted alongside descriptive long-form flags.

Connect mode (default) reads *stdin*, sends it to the remote, and writes the response to *stdout*. On stdin EOF, the write half of the socket is closed so request/response protocols like HTTP do not hang. Listen mode behaves the same way once a connection is accepted, but exits after that single connection.

Check mode (**-z** / **--check**) opens a TCP connection to each specified port, then closes it. Open ports are printed one per line to *stdout*. Closed and timed-out ports go to *stderr* only under **--verbose**. The exit code reflects the worst result seen across all probed ports.

# OPTIONS

**--listen**, **-l**
:   Listen for one inbound connection (TCP) or one datagram (UDP). Exits after the single exchange.

**--check**, **-z**
:   Probe port(s) and exit without exchanging data.

**--udp**, **-u**
:   Use UDP. Default is TCP.

**--tls**, **--ssl**
:   Wrap the TCP connection in TLS. Client mode only; server-side TLS is not supported.

**--insecure**
:   Skip TLS certificate validation. A warning is printed to stderr. Useful for self-signed development servers.

**--ipv4**, **-4**
:   Force IPv4 address resolution.

**--ipv6**, **-6**
:   Force IPv6 address resolution.

**--timeout** *SEC*, **-w** *SEC*
:   Connect and idle timeout in seconds (1-3600). Default is 10 seconds in check mode; unlimited elsewhere.

**--bind** *ADDR*
:   Listener bind address. Default is all interfaces.

**--no-shutdown**
:   Do not half-close the socket on stdin EOF. Only needed for protocols that treat half-close as a full disconnect.

**--verbose**, **-v**
:   In check mode, also print closed/timeout ports to stderr.

**--json**
:   Emit a JSON summary to stderr after the run.

**--describe**
:   Output structured tool metadata as JSON (flags, examples, composability) and exit.

**--color**
:   Force coloured output, overriding **NO_COLOR**.

**--no-color**
:   Disable coloured output.

**--help**
:   Show help and exit.

**--version**
:   Show version and exit.

# PORT SPECIFIERS

Check mode accepts single ports, ranges, lists, or a combination:

    nc -z host 80                       # single
    nc -z host 80-1000                  # range (inclusive)
    nc -z host 80,443,8080              # list
    nc -z host 80-100,443,8080-8090     # mixed

All ports must be 1-65535.

# EXIT CODES

**0**
:   Success — connection completed, all probed ports open, or listener served one exchange.

**1**
:   Connection refused, DNS failure, host unreachable, bind failure, any closed port in check mode, or TLS handshake failure.

**2**
:   Timeout during connect or port check.

**125**
:   Usage error (bad arguments).

**126**
:   Permission denied (e.g. bind to privileged port without elevation).

**130**
:   Interrupted (Ctrl-C).

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# HALF-CLOSE BEHAVIOUR

When stdin reaches EOF in connect or listen mode, **nc** calls **Socket.Shutdown(Send)** so the peer sees end-of-stream on its read side. Without this, request/response protocols like HTTP hang — the peer waits for more request bytes before responding. Use **--no-shutdown** only for the rare protocols that treat half-close as a full disconnect.

# OUTPUT STREAMS

**stdout**
:   Bytes received from the remote (connect/listen modes) or one line per open port (check mode).

**stderr**
:   Status messages, errors, warnings, and the **--json** summary when enabled.

# EXAMPLES

    nc -z target.com 443

    nc -z target.com 80,443,5432 --json

    echo "GET / HTTP/1.0" | nc target.com 80

    nc --tls api.example.com 443

    nc -u dnsserver 53 < query.bin

    nc -l 8080

    nc target.com 80 < request.bin > response.bin

    nc --describe

# SEE ALSO

**wargs**(1), **whoholds**(1), **files**(1), **man**(1)
