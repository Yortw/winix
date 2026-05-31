# demux — design

**Date:** 2026-05-31
**Status:** Approved (brainstorm complete; pending implementation plan)
**Tool:** `demux` — package `Winix.Demux`, class lib `src/Winix.Demux`, console app `src/demux`
**Tagline:** *split one stream into many by pattern*

## 1. Positioning

`demux` is the missing **partition verb** for streams: a streaming `stdin → stdout` pipe
filter that routes each input line to one of N sinks (files **or** commands) by regex match,
and passes everything unmatched through its own stdout. Where `tee` *copies* one stream to many
sinks and `pee` *copies* to many commands, `demux` *routes* — each record goes to a chosen sink.

Pretty per-route summary for humans on stderr; `--json` for scripts and CI.

### Landscape (why it earns a place)

| Environment | Native one-pass *pipe-filter* partition? | Notes |
|---|---|---|
| cmd.exe | ❌ nothing | multiple `findstr` passes only (re-reads input each time) |
| PowerShell | ❌ `switch` is a *statement*, not a pipe filter | can't be piped into, can't sit mid-pipeline, case bodies are script blocks not command sinks |
| bash / Linux / macOS | ✅ `awk` | `awk '/re/{print>"f"}'` / `print|"cmd"` — a real filter, but cryptic; few know the command-pipe form |

The value is **not** a hard gap on every platform — `awk` covers Unix (cryptically) and is the
genuine competitor there. The justification is the same tier as `url`: **cross-platform
consistency + readability over awk + agent-friendliness** (a clean `demux --to … --json` is
more reliable for an agent to emit than correct cryptic awk), *plus* a real Windows gap (no
native pipe-filter partition exists in cmd or pwsh). Accepted eyes-open that this is a
consistency/ergonomics play, not an empty-niche fill.

### Name

`demux` = demultiplexer — the precise term for one-input → one-of-N-outputs-by-selector, which
is exactly the default (first-match) behaviour. The literal names are all collision-blocked:
`switch` (PowerShell keyword), `route` (`route.exe`), `split`/`sort` (coreutils). `demux` is
collision-free on every shell, distinctive, and searchable. Recognition gap (it's a technical
term) is mitigated by the tagline and the suite's `--describe`/`llms.txt`/`docs/ai` discovery
machinery — the name is spent on *findability*, which nothing else covers, since guessability is
already covered by the tagline and `--describe`.

## 2. CLI surface

```
demux [--field N] [--delimiter CHAR] [--all] [--append] [--exit-on-child-error] \
  --to   PATTERN FILE \
  --exec PATTERN CMD \
  [--to PATTERN FILE | --exec PATTERN CMD] ... \
  [--default-to FILE | --default-exec CMD]
```

Each route is a **single self-contained flag carrying both its predicate and its target** — there
is no cross-flag pairing, so a route can never be left half-specified (a dangling predicate or an
orphan target is unrepresentable, not merely a validation error). Route flags are repeatable and
order-independent.

| Flag | Operands | Meaning |
|---|---|---|
| `--to PATTERN FILE` | 2 | Route lines matching regex PATTERN to FILE (opened once). Repeatable. |
| `--exec PATTERN CMD` | 2 | Route lines matching PATTERN to a command spawned once via the platform shell; matching lines fed to its stdin. Repeatable. |
| `--default-to FILE` | 1 | Unmatched records → FILE. |
| `--default-exec CMD` | 1 | Unmatched records → a spawned command. |
| `--field N` | 1 | Test the regex against column *N* (1-based) instead of the whole line. |
| `--delimiter CHAR` | 1 | Field delimiter (default: runs of whitespace, awk-style). |
| `--all` | 0 | Broadcast: route to *every* matching route (default is first-match). |
| `--append` | 0 | File targets append instead of truncate (default: truncate, like `>`). |
| `--exit-on-child-error` | 0 | A watched child's non-zero exit makes `demux` exit `2` (default: report only). |
| standard | — | `--help`, `--version`, `--json`, `--describe`, `--color`, `--no-color`. |

At least one route (`--to`/`--exec`) is required; at most one `--default-*` may be given. Omit
both `--default-*` → unmatched records flow to stdout.

**PATTERN is a bare .NET regular expression** — *not* slash-delimited (grep convention). A leading
or trailing `/` is matched literally, not treated as a delimiter. Quote it to protect regex
metacharacters from the shell. Compiled via ShellKit's `SafeRegex` (ReDoS-safe).

Example:
```
cat app.log | demux \
  --to   'ERROR'     errors.log \
  --exec 'WARN'      'logger -p warning' \
  --to   '^[0-9]+ ms' slow.tsv \
  --default-exec     'gzip > rest.gz'
```

## 3. Semantics & data flow

- **Record model:** line-oriented (`\n`-delimited), read from stdin **streaming** — one line at a
  time, never buffering the whole input. Supports huge files and live streams (`tail -f | demux`).
- **Per line:**
  1. If `--field N`: split on the delimiter, test the regex against field *N* (1-based). Out-of-range
     field → no match (counts as unmatched, so it is visible, not silently dropped). Else test the
     whole line.
  2. Evaluate routes in declaration order. **First-match** (default): route to the first matching
     route's sink and stop. **`--all`**: route to every matching route's sink.
  3. **Unmatched** → stdout, or the `--default-to`/`--default-exec` sink if configured.
- Sinks always receive the **full original line** (with newline), not just the tested field.

## 4. Sinks

- **`FileSink` (`--to`)** — opened once at startup; truncates by default, appends under `--append`.
- **`CommandSink` (`--exec`)** — spawned once at startup via the platform shell (`sh -c` on Unix,
  `cmd /c` on Windows), because targets like `gzip > rest.gz` or `tee err.log` rely on shell
  features. Matching lines are fed to the child's stdin as they arrive; stdin is closed at EOF and
  the child reaped. The command is shell-interpreted — it is the *author's* own command (same trust
  model as `sh -c`), documented as such. Spawning uses `ProcessStartInfo.ArgumentList` (shell +
  `-c`/`/c` + the command as a single argument), honouring the no-arg-string-building convention.
- **`StdoutSink`** — the implicit passthrough sink for unmatched records.

**Line-byte preservation:** sinks write each line followed by an explicit `\n` (never `WriteLine`),
so LF input is not rewritten to CRLF on Windows and a terminator-less final line is not silently
altered (ADR D13).

**Concurrency:** each `CommandSink` drains a bounded queue to its child on a dedicated background
writer thread, so one slow or stuck child applies backpressure only to itself and can never
deadlock the router or starve siblings; `Close` waits for the child with a timeout and kills it
(recording a sentinel) if it overruns (ADR D11).

**File-target safety:** all `--to`/`--default-to` paths are probed for writability *before* any sink
truncates a file, so a setup failure (→ 126) never destroys an earlier file's contents (ADR D12).

### Failure handling (diagnosability-critical)

If an `--exec` child exits early or closes its stdin, further writes raise a broken-pipe error.
`demux` **catches per-sink, marks that route dead, stops writing to it, counts undelivered records,
and keeps every other route running.** A dead route never crashes `demux` or starves its siblings.
Dead routes and undelivered counts surface in the summary. The write path is wrapped so diagnostic
reporting can never mask the real data flow.

## 5. Architecture

Standalone tool: class library `Winix.Demux` + thin console app `src/demux` (parse, build, run,
set exit code). No speculative shared "plumbing" library — the reusable orchestration units
(line-feeding child sink, broken-pipe handling) are self-contained within `Winix.Demux` and
**promotable** to a shared library when a second Class-4 tool (e.g. `merge`) actually exists.

Components:
- **`RouteSpec`** — compiled `SafeRegex` predicate + target (`File`/`Exec` + value).
- **`ArgParser`** — assembles the 2-operand route-flag grammar (`--to`/`--exec` each consume
  PATTERN + TARGET; `--default-*` consume one) over ShellKit's `CommandLineParser` via a thin custom
  pass (precedent: `schedule`/`url`/`qr`). Validates: ≥ 1 route present, ≤ 1 `--default-*`, every
  route flag has both operands, each regex compiles, `--field ≥ 1`. Note the self-contained route
  flags eliminate the orphan/dangling-route validation class entirely.
- **`ISink`** + `FileSink`, `CommandSink`, `StdoutSink` — `Write(line)` / `Close()`, each tracking
  delivered/undelivered counts and dead state.
- **`Router`** — the core loop (read → predicate → select route(s) → dispatch). Pure enough to
  unit-test with in-memory sinks (no files, no processes).
- **`RoutingSummary`** — per-route counts, unmatched count, dead routes; rendered to stderr
  (human) or `--json`.

Reuse from ShellKit: `SafeRegex`, `ConsoleEnv`, `AnsiColor`, `JsonHelper`, `ExitCode`.

**Output routing:** `demux`'s stdout carries passthrough *data*, so per the suite convention for
passthrough tools (`wargs`/`peep`/`nc`), the `--json` envelope and the human summary go to
**stderr**. `NO_COLOR` respected.

## 6. Exit codes (POSIX-ish)

| Code | Meaning |
|---|---|
| `0` | Success — all input routed and delivered. |
| `1` | **Partial delivery failure** — a route went dead mid-run / records undelivered. **Data lost.** (CI notices; details on stderr.) |
| `2` | **Watched child failed** — under `--exit-on-child-error`, at least one `--exec` child exited non-zero. Delivery was complete; no data lost. |
| `125` | Usage error — bad args, no routes, bad regex, a route flag missing an operand, more than one `--default-*`, `--field < 1`. |
| `126` | Setup failure — a `--to` file could not be opened, or the shell could not be launched. |

`1` and `2` are deliberately distinct because they call for different operator responses
(`1` → a sink died, check the child and whether data was lost; `2` → delivery was fine, your
watched command failed its own check). **Precedence:** if both conditions occur in one run — a
route died *and* a watched child exited non-zero — **`1` wins**, because data loss is the more
serious condition.

A child's own non-zero exit is reported in the summary but does **not** by itself fail `demux`
unless `--exit-on-child-error` is set (demux's job is routing, not the child's success). Note that
when `--exec` runs via the shell, a "command not found" surfaces as the *shell's* `127` child
exit (reported in the summary, and → exit `2` under `--exit-on-child-error`), not as demux's
`126` setup failure — `126` is reserved for demux being unable to open a `--to` file or launch
the shell process itself.

## 7. Testing

- **`Router`** — unit tests with in-memory sinks: first-match, `--all`, `--field`, out-of-range
  field, unmatched→stdout, `--default-*`, multi-match under `--all`.
- **`ArgParser`** — grammar assembly, all validation paths → exit 125.
- **`FileSink`** — line writes, truncate vs `--append`.
- **`CommandSink`** — **integration tests with real child processes** (platform-gated
  `SkippableFact` + `Skip.IfNot`), including the broken-pipe case: spawn a child that exits early,
  assert the route is marked dead, siblings keep running, undelivered counted, exit `1`. Per the
  protocol-fake-test caution in `CLAUDE.md`, the child is **not** faked — wire correctness (stdin
  delivery, EPIPE) only shows against a real process.
- **Exit codes** — distinct-cause coverage: `--exit-on-child-error` with a child that consumes all
  input then exits non-zero → exit `2` (delivery complete); and the **precedence** case — a run
  where a route dies *and* a watched child exits non-zero → exit `1` wins over `2`.
- Cross-platform shell-spawn (`sh -c` vs `cmd /c`) gets platform-gated integration coverage.
- Manual cmd + pwsh + bash smokes per the suite's manual-test rule (in-process tests can't
  reproduce real shell behaviour).

## 8. Deferred / out of scope (v1)

| Topic | Why deferred |
|---|---|
| NUL-delimited records (`-0`) | Line-oriented covers the dominant case; add if demand. |
| Shared Class-4 "plumbing" library | YAGNI until a second plumbing tool (`merge`, etc.) exists; units are promotable. |
| `merge`/`fanin` and other Class-4 primitives | Separate tools, separate brainstorms. |
| Capture/exit-on-match CI flags (hcat-style) | Not obviously needed for a router; revisit if asked. |
