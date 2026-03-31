# peep — AI Agent Guide

## What This Tool Does

`peep` runs a command repeatedly and displays output on a refreshing screen. It supports interval polling, file-watch triggers (re-run when files change), diff highlighting between runs, time-machine history navigation, and auto-exit conditions. Use it whenever you need to monitor command output over time or respond to file changes without writing a shell loop.

## Platform Story

Cross-platform. On **Windows**, there is no native `watch` or `entr` equivalent — `peep` fills both gaps in one tool. On **Unix/macOS**, `watch` handles interval polling and `entr` handles file-watch triggers, but they are separate tools with different interfaces. `peep` combines both behaviours with consistent flags across platforms, plus extras like diff mode and time-machine history that neither original offers.

## When to Use This

- Monitoring `kubectl get pods` or `docker ps` until a state changes: `peep --exit-on-match "Running" kubectl get pods`
- Re-running tests when source files change: `peep -w "src/**/*.cs" dotnet test`
- Watching a build's output continuously: `peep dotnet build`
- Tailing a command that doesn't stream (unlike `tail -f`): `peep -n 1 cat app.log`
- Waiting for a condition: `peep --exit-on-success curl -sf https://api.example.com/health`
- CI readiness checks: `peep --exit-on-success --json -n 5 dotnet test 2>result.json`

Prefer `peep -w` over shell file-watch loops — it handles debouncing, gitignore filtering, and multiple glob patterns correctly on all platforms.

## Common Patterns

**Watch a command every 2 seconds (default interval):**
```bash
peep git status
```

**Re-run tests on source file changes — no polling:**
```bash
peep -w "src/**/*.cs" -w "tests/**/*.cs" dotnet test
```

**Wait for a pod to become Ready, then exit:**
```bash
peep --exit-on-match "1/1.*Running" kubectl get pods
```

**Watch build duration trend (combine with timeit):**
```bash
peep -- timeit dotnet build
```

**Exit as soon as the build succeeds:**
```bash
peep --exit-on-success -n 10 -- dotnet build
```

**Diff mode — highlight what changed between runs:**
```bash
peep -d kubectl get pods
```

**Non-interactive mode for scripts — JSON summary on exit:**
```bash
peep --json --exit-on-success -n 5 -- dotnet test 2>result.json
echo $?
```

## Composing with Other Tools

**peep + timeit** — track how a command's runtime evolves:
```bash
peep -- timeit -1 dotnet build
```

**peep + files** — watch for newly created files matching a pattern:
```bash
peep -- files . --glob '*.log' --newer 5m
```

**peep + kubectl/git** — the classic DevOps watch patterns:
```bash
peep -n 5 git log --oneline -10
peep -d -n 10 kubectl get deployments
```

**peep --json + jq** — parse exit result in a script:
```bash
peep --json --exit-on-success -n 5 -- dotnet test 2>&1 >/dev/null | jq '.exit_code'
```

## Gotchas

**Interactive mode requires a terminal.** `peep` renders to the terminal using ANSI sequences. If stdout is redirected (piped or to a file), interactive features (keyboard controls, diff highlighting, time-machine) are unavailable. Use `--json` and `--exit-on-*` flags for scripted use.

**File watcher uses glob patterns, not paths.** `--watch "src/**/*.cs"` monitors files matching that glob under the current directory. The glob is not a filesystem path; quote it to prevent shell expansion.

**--watch respects .gitignore by default.** File changes in gitignored paths (e.g. `obj/`, `bin/`) do not trigger re-runs. Use `--no-gitignore` to disable this.

**--exit-on-match takes a regex, not a glob.** The pattern is matched against the full output text. Use `.*` for wildcards, not `*`.

**--once is for single-run display.** `peep --once` runs the command once, displays the output formatted like a peep screen, and exits. Useful for a consistent display format without looping.

**Keyboard controls are terminal-only.** Press `?` for the in-app help overlay. `q`/`Ctrl+C` to quit, `Space` to pause, `Left`/`Right` for time-machine history.

## Getting Structured Data

`peep` writes a JSON summary to **stderr** on exit with `--json`:

```bash
peep --json --exit-on-success -n 5 -- dotnet test
```

Summary fields: `tool`, `version`, `exit_code`, `exit_reason`, `run_count`, `last_output` (only with `--json-output`).

`--json-output` extends `--json` to include the last captured command output in the JSON envelope — useful when you need both the result and the output text in one document.

**--describe** — machine-readable flag reference:
```bash
peep --describe
```
