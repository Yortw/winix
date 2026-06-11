% ONLINE(1) Winix | User Commands
% Troy Willmot
% 2026-06-11

# NAME

online - block until the internet — or an endpoint — is actually healthy

# SYNOPSIS

**online** [*options*]

# DESCRIPTION

**online** is a network-readiness gate for scripts and agents. It polls until every requested check passes, then exits 0. Use it instead of writing your own poll loop.

By default (bare **online**), **--internet** is implied: a layered, captive-portal-aware check that confirms the internet is actually usable, not merely that a network interface is up.

When multiple checks are requested (**--internet** plus one or more **--url** checks), the gate opens only when **every** check passes in the **same poll cycle**.

The HTTP client does not follow redirects. A captive portal's 302 redirect is therefore seen as a non-204 response and correctly treated as "not online". A **--url** target that redirects will not match the **2xx** default and will keep waiting — use **-v** to see the actual status returned.

# OPTIONS

**--internet**
:   Run a layered, captive-portal-aware internet check: (1) OS route check (fast negative — no network traffic if the interface is down); (2) DNS resolves the probe endpoint host; (3) HTTP GET returns status **204 No Content** (a captive portal returns 200 or 302, never 204). Default when no check flag is given.

**--url** *URL*
:   Wait until *URL* returns a status matching **--status**. 5xx responses, 429, connection failures, and non-matching statuses all cause the check to keep waiting. Repeatable.

**--endpoint** *URL*
:   Override the built-in 204 connectivity endpoints used by the internet check. Requires **--internet**. Using **--endpoint** without **--internet** (i.e. with only **--url** checks) is a usage error (exit 125). Repeatable.

**--status** *SPEC*
:   Expected HTTP status for **--url** checks. Forms: **2xx** (any 2xx), comma-separated list **200,204**, or range **200-299**. Default: **2xx**. Using **--status** without **--url** is a usage error (exit 125).

**--timeout** *DURATION*
:   Total wait budget. Accepts a number followed by a unit suffix: **ms** (milliseconds), **s** (seconds), **m** (minutes). **0** means wait forever. Default: **10m**. On expiry the tool exits with code 124.

**--interval** *DURATION*
:   Sleep between poll cycles. Same duration format as **--timeout**. Default: **2s**.

**--probe-timeout** *DURATION*
:   Per-probe DNS/HTTP timeout. Same duration format as **--timeout**. Default: **3s**.

**--once**
:   Run exactly one poll cycle and exit immediately. Exit 0 if every check is healthy; exit 1 if any check fails. Does not wait or retry.

**-v**, **--verbose**
:   Print per-attempt diagnostics to stderr, including which checks passed or failed and why.

**--json**
:   Write the result envelope to stdout as JSON. Human summary and verbose lines still go to stderr. Fields: **tool**, **version**, **ready**, **timed_out**, **elapsed_ms**, **attempts**, **checks[]** (each check: **kind**, **target**, **ok**, **detail**).

**--color**[=_WHEN_]
:   Coloured output: **auto** (default when omitted), **always**, or **never**.

**--no-color**
:   Disable coloured output.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

**--describe**
:   Emit machine-readable JSON metadata (flags, examples, composability).

# EXIT CODES

**0**
:   Ready — every requested check healthy.

**1**
:   **--once** only: checked once, not ready right now.

**124**
:   Timed out before ready (wait mode).

**125**
:   Usage error — bad arguments, unparseable duration/status, malformed URL, **--endpoint** given without **--internet**, or **--status** given without **--url**.

**126**
:   Unexpected error (tool fault).

**130**
:   Interrupted (Ctrl+C).

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# EXAMPLES

    online

    online --once

    online --internet --url https://api.example.com/health && resume-work

    online --url https://api.example.com/health --status 200,204

    online --timeout 2m --verbose

    online --internet --endpoint https://myprobe.internal/generate_204

    online && retry --times 3 dotnet test

# NOTES

**Timeout overshoot.** The deadline is checked between poll cycles, not within a probe. If a probe is slow, the actual wait can exceed **--timeout** by up to one cycle's probe time, and **elapsed_ms** in the JSON envelope may slightly exceed the timeout value. This is invisible at the 10-minute default; it only matters for very short timeouts combined with slow probes.

**DNS and single-family networks.** The DNS rung of the internet check accepts an address of any address family (IPv4 or IPv6). On a single-family network, a host name that resolves to the unusable family passes the DNS rung and correctly falls through to "not online" at the HTTP rung — but the **-v** DNS line may report success even though the address returned is unusable. The HTTP failure will still be reported correctly.

# SEE ALSO

**nc**(1), **retry**(1), **timeit**(1)
