# demux — AI Agent Guide

## What This Tool Does

`demux` is a streaming `stdin → stdout` pipe filter that routes each input line to one of N sinks (files or commands) by regex match, and passes everything unmatched through its own stdout. It is the **partition verb** for pipes: where `tee` copies one stream to many, `demux` routes — each record goes to one chosen sink. Tagline: *split one stream into many by pattern.*

Input is read one line at a time (streaming, never buffering the whole input), so it works on large files and live streams (`tail -f | demux …`).

## When to Use This

- Routing a stream to multiple files or commands by regex in **one pass** — without re-reading stdin:
  `cat app.log | demux --to ERROR errors.log --to WARN warnings.log`
- The **cross-platform readable alternative to awk's partition pattern** (`awk '/re/{print>"f"}'` / `print|"cmd"`) — same semantics, cleaner flags, works on all platforms including Windows where awk may not be available.
- **Windows pipe-filter partition gap** — `cmd.exe` has nothing, PowerShell `switch` is a statement (not a pipe filter, can't be piped into, can't sit mid-pipeline). `demux` fills that gap as a genuine pipe-filter on all platforms.
- When you want **diagnosable, structured output**: `--json` gives per-route delivered/undelivered counts, dead-route flags, and child exit codes — useful in CI pipelines.
- When routing should be **agent-friendly**: clean `--to`/`--exec` flags are more reliable to emit than nested awk quoting, especially in generated commands.
- **Field-based routing on structured data**: `--field N --delimiter CHAR` to route on a specific column of a TSV, log line, etc.

## When NOT to Use This

- **Single-sink filtering** (pass/drop by pattern) — use `grep`, `Select-String`, or `files --content`.
- **Transforming lines** (substitutions, field extraction) — use `awk` or `sed`; `demux` only routes, it does not transform.
- **Copying one stream to N identical sinks** — use `tee`; `demux` routes to one-of-N, not all-N simultaneously (unless `--all` is given, but that still requires explicit routes).
- **Non-line-oriented data** — `demux` is line-oriented; binary or NUL-delimited data is not supported in v1.

## Basic Invocation

```bash
# Route ERROR lines to a file, pass everything else through to the next stage
cat app.log | demux --to ERROR errors.log | gzip > main.log.gz

# Multi-route split: file, command, TSV, compressed rest
cat app.log | demux \
  --to   'ERROR'        errors.log \
  --exec 'WARN'         'logger -p warning' \
  --to   '^[0-9]+ ms'  slow.tsv \
  --default-exec        'gzip > rest.gz'

# Copy error lines to the clipboard, file the rest (composes with clip)
demux --exec ERROR clip --default-to rest.log

# Route on column 3 (tab-delimited)
demux --field 3 --delimiter $'\t' --to '^4' client_errors.tsv --to '^5' server_errors.tsv
```

The routing summary (`demux summary: …`) goes to **stderr**. `demux`'s stdout carries only passthrough (unmatched) data.

## Flag Surface

| Flag | Operands | Description |
|---|---|---|
| `--to PATTERN FILE` | 2 | Route lines matching PATTERN to FILE (repeatable). |
| `--exec PATTERN CMD` | 2 | Route lines matching PATTERN to CMD's stdin, shell-spawned (repeatable). |
| `--default-to FILE` | 1 | Unmatched records → FILE. |
| `--default-exec CMD` | 1 | Unmatched records → CMD. Omit both → unmatched → stdout. |
| `--field N` | 1 | Test the regex against column N (1-based) instead of the whole line. |
| `--delimiter CHAR` | 1 | Field delimiter (default: runs of whitespace). |
| `--all` | 0 | Broadcast: route to every matching route (default: first-match). |
| `--append` | 0 | File targets append instead of truncate. |
| `--exit-on-child-error` | 0 | A watched child's non-zero exit makes demux exit 2. |
| `--json` | 0 | Emit a JSON summary to stderr. |
| `--color` | 0 | Force coloured output (overrides terminal auto-detection). |
| `--no-color` | 0 | Disable coloured output. Respects `NO_COLOR`. |
| `--describe` | 0 | Structured JSON metadata for AI agents. |
| `--help`, `-h` | 0 | Show help and exit. |
| `--version` | 0 | Show version and exit. |

PATTERN is a **bare .NET regular expression** — not slash-delimited. A leading or trailing `/` is matched literally. Quote patterns to protect shell metacharacters (e.g. `'ERROR|FATAL'`, `'^[0-9]+ ms'`).

At least one `--to` or `--exec` route is required. At most one `--default-to` or `--default-exec` may be given.

## JSON Output (`--json`)

The JSON envelope is written to **stderr** (demux's stdout carries passthrough data).

```json
{
  "tool": "demux",
  "version": "0.4.0",
  "exit_code": 1,
  "exit_reason": "partial_delivery_failure",
  "routes": [
    { "label": "ERROR → errors.log", "delivered": 12, "undelivered": 0, "dead": false },
    { "label": "WARN → logger -p warning", "delivered": 5, "undelivered": 3, "dead": true, "child_exit_code": -1, "killed_after_timeout": true },
    { "label": "(stdout)", "delivered": 204, "undelivered": 0, "dead": false }
  ]
}
```

| Field | Type | Description |
|---|---|---|
| `tool` | string | `"demux"` |
| `version` | string | Tool version |
| `exit_code` | int | Process exit code (0/1/2) |
| `exit_reason` | string | `"success"`, `"partial_delivery_failure"`, or `"watched_child_failed"` |
| `routes` | array | Per-sink outcome (see below) |

Route object fields:

| Field | Type | Description |
|---|---|---|
| `label` | string | Sink description (pattern → target) |
| `delivered` | int | Lines successfully written to this sink |
| `undelivered` | int | Lines lost because this sink was dead |
| `dead` | bool | True if the sink failed mid-run |
| `child_exit_code` | int | Present for `--exec` routes; `-1` = killed after timeout |
| `killed_after_timeout` | bool | Present and `true` when `child_exit_code` is `-1` |

Parse stderr output with `--json` to get structured counts without scraping the human summary.

## Exit Code Contract

| Code | Meaning |
|---|---|
| 0 | Success — all input routed and delivered. |
| 1 | **Partial delivery failure** — a route went dead, records undelivered. Data lost. |
| 2 | **Watched child exited non-zero** — `--exit-on-child-error` was set; delivery complete, no data lost. |
| 125 | Usage error — bad args, no routes, bad regex, missing operand, duplicate `--default-*`, or `--field < 1`. |
| 126 | Setup failure — a `--to` file could not be opened, or the shell could not be launched. |

**Precedence:** if a route dies *and* a watched child exits non-zero in the same run, exit code `1` wins (data loss is the more serious condition). This means you can distinguish "delivery problem" from "child problem" cleanly by checking the exit code, without reading stderr.

A child's non-zero exit does **not** fail `demux` unless `--exit-on-child-error` is set. Note: "command not found" surfaces as the shell's `127` child exit (→ exit `2` under `--exit-on-child-error`), not as demux's `126`. Exit `126` is reserved for `demux` failing to open a file or launch the shell itself.

## Agent Friendliness

- Clean, self-contained route flags (`--to PATTERN FILE`, `--exec PATTERN CMD`) are structurally impossible to leave half-specified — a dangling predicate or orphan target cannot be expressed. This reduces the class of agent-generated usage errors.
- `--json` gives machine-parseable per-route counts on stderr; stdout is clean passthrough data, composable with the next pipeline stage.
- PATTERN is a bare .NET regex without delimiter characters — no quoting gymnastics beyond normal shell quoting.
- Spawning `--exec` via the platform shell means you can write `'gzip > rest.gz'` or `'tee log.txt'` without inventing a mini shell yourself.

## Limitations

- **Line-oriented:** one very large line (no `\n`) is held in memory until EOF — a per-line bound, not a whole-input bound.
- **Alive-but-stalled `--exec` child:** a child that never reads while downstream stdout is backpressured can stall the run. Only the shutdown `WaitForExit` is timeout-bounded (10 s).
- **`--exec` children inherit demux's stdout/stderr:** a tee-style child writes to `demux`'s own stdout, interleaved with passthrough data.
- **No Ctrl+C / SIGKILL child cleanup in v1:** `--exec` children may be orphaned on interrupt.

## Metadata

Run `demux --describe` for structured metadata (option flags, types, examples, composability, exit codes, JSON fields). Note: the two-operand route flags (`--to`, `--exec`, `--default-to`, `--default-exec`) are documented in the `--help` *Routes* section rather than as typed entries in the `--describe` `options` array — ShellKit's structured options model covers single-operand flags only.
