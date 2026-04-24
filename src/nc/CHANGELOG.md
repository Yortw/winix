# Changelog

All notable changes to **nc** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.4.0] - 2026-04-24

Three rounds of fresh-eyes code review landed substantive behaviour fixes
across the client, listener, check, and TLS paths. Round 1 corrected the
big-ticket silent-failure classes; round 2 tightened the check-mode exit
contract and the TLS handshake deadline; round 3 closed two remaining
Critical defect classes (AF honoured, broad exception safety-net) plus a
large batch of Important diagnosability gaps.

### Added

- `--ipv4` / `--ipv6` flags now actually honoured in TCP Connect and
  `--check` modes (round-3 Critical). Previously the flags were advertised
  in `--help` / `--describe` but the resolver ran dual-stack regardless.
- `--check --json` now suppresses per-port text on stdout so downstream
  parsers get a single-stream JSON envelope.
- Check-mode non-verbose stderr summary now covers both all-error AND
  all-timeout cases — `nc -z blackhole 80,443` no longer exits 2 with
  empty stdout and empty stderr.
- `nc --check` emits a JSON summary where `exit_reason` distinguishes
  `all_failed` from `some_failed` when probes return a mix of errors and
  successes.
- `nc --listen -w N` now writes a stderr note when accept times out, so
  the user sees WHY the tool exited with 2 rather than a silent exit.
- `nc -u host port -w N` now writes a stderr warning when no UDP response
  arrives within the timeout (BSD nc precedent is exit 0; the warning
  makes the empty-output case intentional rather than ambiguous).
- JSON error envelope emitted from the safety-net catches when `--json`
  is set. Automation no longer gets a bare stderr crash line on
  unexpected exceptions.
- New JSON field `error` advertised in `--describe` (present only on the
  catch-all envelope).
- New `exit_reason` enum values: `pump_failed` (emitted when the bidirectional
  relay hits an unexpected exception class — ObjectDisposedException or
  InvalidOperationException from a racy half-close — instead of escaping to
  the safety-net), `accept_failed` (listener-side parity for non-SocketException
  accept failures), `unexpected_error` (emitted on the Main safety-net's JSON
  envelope when `--json` is set and an exception escaped all per-site catches),
  and `connect_failed` (TCP/UDP client-side parity for non-SocketException
  connect failures).

### Changed

- TLS handshake timeout is now scoped to the handshake itself rather than
  sharing the TCP connect deadline. A hanging TLS server with `-w N` set
  now times out at N seconds instead of blocking indefinitely.
- TLS close uses `SslStream.ShutdownAsync` (`close_notify`) before the
  TCP write-half shutdown, so strict peers don't log truncation alerts.
- `--bind` now rejects unparseable IPs at parse time (was silently
  falling through to `IPAddress.Any`, defeating the security intent).
- `--bind` now rejects AF mismatch with `--ipv4` / `--ipv6` — e.g.
  `nc -l --ipv6 --bind 127.0.0.1 8080` is a usage error rather than
  silently ignoring `--ipv6`.
- `--bind` rejects hostnames — IP literals only (matches BSD `nc -s`).
- Check-mode port-spec parse failures (`nc -z host invalid`) now return
  a clean usage error instead of a stack trace.

### Fixed

- Non-SocketException / non-OperationCanceledException exception types
  from ConnectAsync / UdpClient.Connect / stdin ReadAsync / stdout
  WriteAsync no longer escape to the safety-net as "unexpected error"
  exit 126. Each path now classifies its failures to a real exit code
  (1 socket_error / 1 io_error / 0 stdout_closed / 1 connect_failed)
  and returns a proper JSON envelope.
- Relay pump now observes both send and receive task outcomes via a
  linked CTS + try/finally. A send-side exception no longer leaves
  the receive task unobserved, and both sides' exceptions are surfaced
  via `ExceptionDispatchInfo` with preserved stack traces.
- Downstream pipe closure during receive (e.g. `nc host 80 | head -c 10`)
  now exits 0 with `exit_reason=stdout_closed`, matching BSD nc's
  SIGPIPE behaviour. Previously mis-classified as `socket_error`.
- `nc -z bad-hostname` no longer aborts the whole scan via Task.WhenAll.
  Per-port errors are now bucketed as Error so the remaining probes run.
- Ctrl-C during UDP Connect send now returns RunResult(130) with partial
  byte counts + duration, so `--json` consumers see a complete envelope.
- NetCatListener UDP path now has user-cancel (Ctrl-C) parity with the
  TCP path — Ctrl-C during `nc -l -u 53` no longer bypasses the JSON
  envelope.
- `RelayPump.ShouldShutdownSend` now reflects actual state — clears
  when the half-close callback fails instead of staying true.
- `onSendComplete` callbacks in NetCatClient and NetCatListener catch
  broadly so a cosmetic half-close failure (racy
  InvalidOperationException, SslStream IOException) cannot mask a
  successful pump outcome.
- TCP accept `SocketException` (Linux interface flap, fd limit) now
  mapped to exit 1 rather than escaping as "unexpected error".
- User-cancel OCE during TLS handshake rethrown so Main's 130 arm fires
  instead of mis-labelling as `tls_failed` / exit 1.

### Pinning tests

46 → 56 → 75+ xUnit tests. Every fix above has at least one test that
would fail if the fix were reverted; naming convention explicitly cites
which review round and finding each test pins.

## [0.2.0] - 2026-04-16

- Initial release.
