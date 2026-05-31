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
consistency + readability over awk + agent-friendliness** (a clean `demux --case … --json` is
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
  --case PATTERN (--to FILE | --exec 'CMD') \
  [--case PATTERN (--to FILE | --exec 'CMD')] ... \
  [--default (--to FILE | --exec 'CMD')]
```

| Flag | Meaning |
|---|---|
| `--case PATTERN` | A regex predicate. Immediately followed by exactly one target (`--to` or `--exec`). Repeatable. |
| `--to FILE` | Target: write matching lines to a file (opened once). |
| `--exec 'CMD'` | Target: spawn a command once via the platform shell; feed matching lines to its stdin. |
| `--default …` | Same target forms; receives unmatched records. Omitted → unmatched flow to stdout. |
| `--field N` | Test the regex against column *N* (1-based) instead of the whole line. |
| `--delimiter CHAR` | Field delimiter (default: runs of whitespace, awk-style). |
| `--all` | Broadcast: route to *every* matching case (default is first-match). |
| `--append` | File sinks append instead of truncate (default: truncate, like `>`). |
| `--exit-on-child-error` | A child's non-zero exit makes `demux` exit non-zero (default: report only). |
| standard | `--help`, `--version`, `--json`, `--describe`, `--color`, `--no-color`. |

Example:
```
cat app.log | demux \
  --case /ERROR/ --to errors.log \
  --case /WARN/  --exec 'logger -p warning' \
  --case '/^\d+ ms/' --to slow.tsv \
  --default --exec 'gzip > rest.gz'
```

## 3. Semantics & data flow

- **Record model:** line-oriented (`\n`-delimited), read from stdin **streaming** — one line at a
  time, never buffering the whole input. Supports huge files and live streams (`tail -f | demux`).
- **Per line:**
  1. If `--field N`: split on the delimiter, test the regex against field *N* (1-based). Out-of-range
     field → no match (counts as unmatched, so it is visible, not silently dropped). Else test the
     whole line.
  2. Evaluate cases in declaration order. **First-match** (default): route to the first matching
     case's sink and stop. **`--all`**: route to every matching case's sink.
  3. **Unmatched** → stdout, or the `--default` sink if configured.
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
- **`ArgParser`** — assembles the paired `--case … --to/--exec …` grammar over ShellKit's
  `CommandLineParser` via a thin custom pass (precedent: `schedule`/`url`/`qr`). Validates every
  `--case` has exactly one target, regex compiles, `--field ≥ 1`, at least one case present.
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
| `1` | Partial failure — a route went dead mid-run / records undelivered. (CI notices; details on stderr.) Also returned when `--exit-on-child-error` is set and a child exited non-zero. |
| `125` | Usage error — bad args, no cases, bad regex, `--case` without a target, `--field < 1`. |
| `126` | Setup failure — a `--to` file could not be opened, or an `--exec` child could not be spawned. |

A child's own non-zero exit is reported in the summary but does **not** by itself fail `demux`
unless `--exit-on-child-error` is set (demux's job is routing, not the child's success).

## 7. Testing

- **`Router`** — unit tests with in-memory sinks: first-match, `--all`, `--field`, out-of-range
  field, unmatched→stdout, `--default`, multi-match under `--all`.
- **`ArgParser`** — grammar assembly, all validation paths → exit 125.
- **`FileSink`** — line writes, truncate vs `--append`.
- **`CommandSink`** — **integration tests with real child processes** (platform-gated
  `SkippableFact` + `Skip.IfNot`), including the broken-pipe case: spawn a child that exits early,
  assert the route is marked dead, siblings keep running, undelivered counted, exit `1`. Per the
  protocol-fake-test caution in `CLAUDE.md`, the child is **not** faked — wire correctness (stdin
  delivery, EPIPE) only shows against a real process.
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
