# retry — AI Agent Guide

## What This Tool Does

`retry` wraps a command and automatically re-runs it when it fails. It supports configurable retry counts, delays, backoff strategies (fixed, linear, exponential), jitter, and exit-code filtering so you can retry only on specific codes or poll until a target code is reached. The child's stdout, stderr, and exit code pass through unmodified — summary output goes to stderr so it does not pollute piped output.

## Platform Story

Cross-platform. **No platform has a native retry command** — Windows, Linux, and macOS all lack one. The conventional substitute is a hand-rolled shell loop (`for i in 1 2 3; do cmd && break; done`), which is fragile, hard to read, and doesn't handle backoff, jitter, or exit-code filtering. `retry` replaces that pattern with a consistent, reliable, cross-platform binary.

## When to Use This

- Retrying flaky test runs in CI: `retry dotnet test`
- Adding resilience to network-dependent commands: `retry --backoff exp --jitter curl -f http://api/health`
- Polling until a service is ready: `retry --until 0 --delay 5s docker ps`
- Retrying a build only on known-transient error codes: `retry --on 1,2 --times 3 make build`
- Making a one-off command more robust without writing a shell loop

Prefer `retry` over shell loops — they silently swallow the exit code on the wrong success condition, don't handle Windows, and are easy to get wrong on exit-code semantics.

## Common Patterns

**Default retry (3 retries, 1s fixed delay):**
```bash
retry dotnet test
```

**More retries, longer delay:**
```bash
retry --times 5 --delay 2s dotnet test
```

**Exponential backoff with jitter — good for remote APIs:**
```bash
retry --times 5 --delay 1s --backoff exp --jitter curl -f http://api/health
```

**Poll mode — retry until exit code is 0:**
```bash
retry --until 0 --delay 5s docker ps
```

**Retry only on specific exit codes:**
```bash
retry --on 1,2 --times 3 make build
```

**JSON output for CI pipelines:**
```bash
retry --json dotnet test
```

**Use -- when child args look like retry flags:**
```bash
retry -- myapp --times 10 --delay 0
```

## Composing with Other Tools

**retry + timeit** — time the entire retry sequence including all attempts:
```bash
timeit retry make test
```

**retry + peep** — file-watch with auto-retry on each change:
```bash
peep -- retry --times 2 make test
```

**retry + wargs** — retry a command for each line of input:
```bash
cat urls.txt | wargs retry --times 3 curl -f {}
```

**retry --json + jq** — extract attempt count from structured output:
```bash
retry --json dotnet test 2>&1 >/dev/null | jq '.attempts'
```

## Backoff Strategies

| Strategy | Delay progression (base = 1s) |
|----------|-------------------------------|
| `fixed`  | 1s, 1s, 1s, 1s |
| `linear` | 1s, 2s, 3s, 4s |
| `exp`    | 1s, 2s, 4s, 8s |

Add `--jitter` to randomise each computed delay to 50–100% of its value — this prevents multiple processes retrying in lock-step (thundering herd).

## Exit-Code Filtering

`--on` and `--until` give precise control over retry semantics:

- **`--on X,Y`** — retry only when the exit code is in the list; pass through any other code immediately. Use this to avoid retrying on permanent errors.
- **`--until X,Y`** — keep retrying until the exit code is in the list. Use for polling: the command is expected to fail repeatedly until it succeeds.
- Both flags accept comma-separated integers.
- `--on` and `--until` cannot be combined.

## Gotchas

**Summary goes to stderr by default.** This is intentional — it keeps the child's stdout clean for piping. Use `--stdout` or redirect stderr if you need the retry summary in a variable or file.

**`--times` counts retries, not total attempts.** `--times 3` means the command runs up to 4 times total (1 initial + 3 retries). This matches the intuitive meaning of "retry 3 times".

**Exit code on exhaustion is the last child exit code.** If all attempts fail, `retry` exits with whatever the last attempt returned — not a fixed code. This preserves the child's error semantics for callers.

**`--` separator is required when child args shadow retry flags.** If your command takes `--times` or `--delay`, put `--` before the command so `retry` stops parsing flags at that point.

**Delay accumulates across attempts.** With exponential backoff and many retries, total wait time grows quickly. `--times 10 --delay 1s --backoff exp` can wait over 17 minutes across all retries.

## Getting Structured Data

`retry` supports JSON output via `--json`:

```bash
retry --json dotnet test
```

JSON is written to **stderr** (use `--stdout` to redirect to stdout). Fields:

- `tool` — `"retry"`
- `version` — tool version string
- `exit_code` — final exit code returned to the caller
- `exit_reason` — `"success"`, `"exhausted"`, `"not_retryable"`, `"until_matched"`, or `"usage_error"`
- `attempts` — total number of times the command was run
- `child_exit_code` — last exit code from the child process

**--describe** — machine-readable flag reference:
```bash
retry --describe
```
