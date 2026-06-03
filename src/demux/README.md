# demux

Route each line of stdin to files or commands by regex — the **partition verb** for pipes. *Split one stream into many by pattern.*

## Description

`demux` is the missing partition verb for streams: a streaming `stdin → stdout` pipe filter that routes each input line to one of N sinks (files **or** commands) by regex match, and passes everything unmatched through its own stdout.

Where `tee` *copies* one stream to many sinks, `demux` *routes* — each record goes to a chosen sink, not every sink. This is the precise demultiplexer model: one input, one-of-N outputs by selector.

### Why it exists

| Environment | Native one-pass pipe-filter partition? | Notes |
|---|---|---|
| cmd.exe | No | Multiple `findstr` passes re-read stdin each time |
| PowerShell | No | `switch` is a statement, not a pipe filter — can't be piped into, can't sit mid-pipeline |
| bash / Linux / macOS | `awk` | `awk '/re/{print>"f"}'` / `print|"cmd"` — a real filter, but cryptic |

`awk` is the genuine Unix competitor and `demux` does not pretend otherwise. The value is **cross-platform consistency + readability over awk + agent-friendliness**: a clean `demux --to … --json` is more reliable for an agent or script to emit than correct nested awk quoting, *plus* there is a genuine pipe-filter partition gap on Windows (neither cmd nor PowerShell covers it).

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/demux
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Demux
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Demux
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
demux [options] --to PATTERN FILE | --exec PATTERN CMD [...]
      [--default-to FILE | --default-exec CMD]
```

At least one `--to` or `--exec` route is required. Routes are evaluated in declaration order.

### Worked example — split an application log

```bash
cat app.log | demux \
  --to   'ERROR'       errors.log \
  --exec 'WARN'        'logger -p warning' \
  --to   '^[0-9]+ ms'  slow.tsv \
  --default-exec       'gzip > rest.gz'
```

- Lines matching `ERROR` go to `errors.log`.
- Lines matching `WARN` are fed to `logger -p warning` (a shell command).
- Lines matching the latency prefix (`^[0-9]+ ms`) go to `slow.tsv`.
- Everything else is compressed via `gzip > rest.gz`.

Each route is first-match by default (add `--all` to broadcast to every matching route).

### Simple two-route split

```bash
# Route error lines to a file; pass everything else through to the next stage
cat pipeline.log | demux --to 'ERROR|FATAL' errors.log | gzip > main.log.gz
```

Unmatched lines pass through `demux`'s stdout, so `demux` composes naturally mid-pipeline.

### Copy errors to the clipboard, file the rest

```bash
demux --exec ERROR clip --default-to rest.log
```

### Field-based routing (structured data)

```bash
# Route on column 3 (tab-delimited) — e.g. split a TSV by status code
demux --field 3 --delimiter $'\t' --to '^4' client_errors.tsv --to '^5' server_errors.tsv
```

## Options

| Flag | Operands | Description |
|---|---|---|
| `--to PATTERN FILE` | 2 | Route lines matching regex PATTERN to FILE (repeatable). |
| `--exec PATTERN CMD` | 2 | Route lines matching PATTERN to a command's stdin, shell-spawned (repeatable). |
| `--default-to FILE` | 1 | Unmatched records → FILE. |
| `--default-exec CMD` | 1 | Unmatched records → a command. Omit both → unmatched → stdout. |
| `--field N` | 1 | Test the regex against column N (1-based) instead of the whole line. |
| `--delimiter CHAR` | 1 | Field delimiter (default: runs of whitespace, awk-style). |
| `--all` | 0 | Broadcast: route to every matching route (default: first-match). |
| `--append` | 0 | File targets append instead of truncate (default: truncate, like `>`). |
| `--exit-on-child-error` | 0 | A watched child's non-zero exit makes demux exit 2. |
| `--json` | 0 | Emit a JSON summary envelope to stderr. |
| `--color[=auto\|always\|never]` | 0 | Coloured output: auto (default when omitted), always, or never. |
| `--no-color` | 0 | Disable coloured output. Respects `NO_COLOR`. |
| `--describe` | 0 | Emit structured JSON metadata for AI discoverability. |
| `--help`, `-h` | 0 | Show help and exit. |
| `--version` | 0 | Show version and exit. |

### PATTERN syntax

PATTERN is a **bare .NET regular expression** — not slash-delimited (that is grep's convention). A leading or trailing `/` is matched literally, not treated as a delimiter. Quote the pattern to protect regex metacharacters from the shell.

Examples of valid patterns: `ERROR`, `^[0-9]+ ms`, `WARN|NOTICE`, `\.(jpg|png)$`.

### Semantics

- **Record model:** line-oriented. Input is read one line at a time — streaming, never buffering the whole input. Works on huge files and live streams (`tail -f | demux …`).
- **Per line:** if `--field N` is set, the regex is tested against column N of the line (split on the delimiter). An out-of-range column counts as unmatched (visible in the summary, never silently dropped). Otherwise the whole line is tested.
- **First-match (default):** the line is routed to the first matching route's sink, then evaluation stops.
- **`--all`:** the line is routed to every matching route's sink.
- **Unmatched:** lines matching no route go to stdout (or to `--default-to`/`--default-exec` if configured).
- **Full line preserved:** sinks always receive the full original line with its newline — not just the tested field.

### `--exec` commands

`--exec` commands are spawned once at startup via the platform shell (`sh -c` on Unix, `cmd /c` on Windows) so that shell features like pipes and redirects work inside the command string (e.g. `gzip > rest.gz`). Matching lines are fed to the child's stdin as they arrive; stdin is closed at EOF and the child is reaped. **The command is shell-interpreted** — it is assumed to be the author's own command, with the same trust model as `sh -c`.

## JSON Output (`--json`)

The JSON summary is written to **stderr** (demux's stdout carries passthrough data).

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
| `routes` | array | Per-sink outcome objects (see below) |

Each route object:

| Field | Type | Description |
|---|---|---|
| `label` | string | Sink description (pattern → target) |
| `delivered` | int | Lines successfully written to this sink |
| `undelivered` | int | Lines lost because the sink was dead |
| `dead` | bool | True if the sink failed mid-run (broken pipe, child crash) |
| `child_exit_code` | int | Present when the sink spawns a child; `-1` = killed after timeout |
| `killed_after_timeout` | bool | Present and `true` when `child_exit_code` is `-1` |

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success — all input routed and delivered. |
| 1 | **Partial delivery failure** — a route went dead mid-run, records undelivered. Data lost. CI should alert. |
| 2 | **Watched child exited non-zero** — under `--exit-on-child-error`, at least one `--exec` child failed. Delivery was complete; no data lost. |
| 125 | Usage error — bad args, no routes, bad regex, a route flag missing an operand, more than one `--default-*`, or `--field < 1`. |
| 126 | Setup failure — a `--to` file could not be opened, or the shell could not be launched. |

**Precedence:** if a route dies *and* a watched child exits non-zero in the same run, exit code `1` wins (data loss is the more serious condition).

A child's non-zero exit is reported in the summary but does not fail `demux` unless `--exit-on-child-error` is set — `demux`'s job is routing, not the child's success. Note that "command not found" surfaces as the shell's `127` child exit (→ exit `2` under `--exit-on-child-error`), not as demux's `126`. Exit `126` is reserved for `demux` failing to open a `--to` file or launch the shell itself.

## Colour

The summary and usage messages are written to **stderr**. `demux`'s stdout carries only passthrough data. Colour is auto-detected (on when stderr is a terminal) and respects `NO_COLOR` (no-color.org). Use `--color` to force it on or `--no-color` to force it off.

## Limitations

- **Line-oriented:** `demux` reads one line at a time. A single line with no `\n` (a blob written without a newline) is held in memory until EOF. This is a per-line bound — streaming many normal lines is fine — but one very large line can consume significant memory.
- **Alive-but-stalled `--exec` child:** if a child process is alive but never reads from its stdin while `demux`'s downstream stdout is also backpressured, the run can stall mid-stream. Only the shutdown `WaitForExit` is timeout-bounded (10 s, then the child is killed). A child that simply exits early is detected and marked dead promptly.
- **`--exec` children inherit demux's stdout/stderr:** a child that echoes its input back to stdout (e.g. a tee-style command) will write to `demux`'s own stdout, interleaved with passthrough lines. This is a composability consequence of shell inheritance, not a bug.
- **No Ctrl+C child cleanup (v1):** if `demux` is interrupted by Ctrl+C or SIGKILL, `--exec` children may be orphaned. They rely on shell job control or inherited-pipe EOF to terminate. A future version may add explicit signal handling.

## Related Tools

- [`wargs`](../wargs/README.md) — build commands from stdin: `wargs` → `xargs` replacement, useful for feeding paths to `demux`.
- [`files`](../files/README.md) — find files to pipe: `files . --name "*.log" | wargs cat | demux …`
- [`clip`](../clip/README.md) — cross-platform clipboard; pairs with `--exec`.

## See Also

- `man demux` (after `winix install man`)
- `demux --describe` for JSON metadata
