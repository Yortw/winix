# winix — AI Agent Guide

## What This Tool Does

`winix` is the installer and lifecycle manager for the Winix CLI tool suite. It installs, updates, uninstalls, and reports the status of all Winix tools by delegating to the platform's native package manager — Scoop or Winget on Windows, Homebrew on macOS, or the system package manager on Linux. Use it when you need to provision or update the Winix suite in a single command rather than managing each tool individually.

## Platform Story

Cross-platform. On **Windows**, `winix` detects whether Scoop or Winget is available and uses it automatically; pass `--via scoop` or `--via winget` to override. On **macOS**, it delegates to Homebrew. On **Linux**, it selects the appropriate package manager or falls back to direct binary download. Use `--dry-run` on any platform to preview exactly what commands would be executed.

## When to Use This

- Provisioning a new machine with all Winix tools: `winix install`
- Installing a specific subset of tools: `winix install timeit squeeze`
- Keeping tools current: `winix update`
- Auditing what is installed and at what version: `winix status`
- Removing all tools from a machine: `winix uninstall`
- Scripting reproducible environment setup: `winix install --dry-run` to verify, then run without `--dry-run`

Prefer `winix` over invoking each tool's package manager command individually when you need consistent, idempotent management across the whole suite.

## Common Patterns

**Install all tools using the auto-detected package manager:**
```bash
winix install
```

**Install only specific tools:**
```bash
winix install timeit peep wargs
```

**Force a specific package manager:**
```bash
winix install --via scoop
winix install --via winget
```

**Preview changes without applying them:**
```bash
winix install --dry-run
winix update --dry-run
```

**Check what is installed and at what version:**
```bash
winix status
```

**Update everything:**
```bash
winix update
```

**Uninstall a specific tool:**
```bash
winix uninstall squeeze
```

## Composing with Other Tools

`winix` is a provisioning tool rather than a data-processing tool, so it does not compose via pipes in the way that `files`, `wargs`, or `treex` do. Its primary use in automation is in setup scripts and CI environments:

**CI environment setup:**
```bash
winix install --dry-run && winix install
```

**Status check in a script:**
```bash
winix status --no-color
```

## Gotchas

**Auto-detection picks the first available package manager.** On Windows machines with both Scoop and Winget installed, Scoop is preferred. Use `--via winget` to override if your organisation standardises on Winget.

**--dry-run shows package manager commands, not Winix internals.** The output shows the exact commands that would be run (e.g. `scoop install winix/timeit`), not Winix's internal logic. This is intentional — it makes the preview auditable.

**Partial failures exit with code 1.** If one tool fails to install but others succeed, exit code is 1. Check the output for per-tool status; the tools that succeeded are usable.

**update only affects installed tools.** Running `winix update` on a machine where only `timeit` is installed will update `timeit` only. It will not install tools that are absent.

**uninstall without a tool list removes all tools.** Passing `winix uninstall` with no tool names removes everything. Use `winix uninstall timeit` to target a specific tool.

**Exit code 126 means no package manager was found.** On a minimal system with no supported package manager, `winix` cannot operate. Install Scoop, Winget, or Homebrew first, or use `--via dotnet` to fall back to .NET global tool installation.

## Getting Structured Data

**--describe** — machine-readable flag reference and metadata:
```bash
winix --describe
```

Output is JSON to stdout and always exits 0. Use this when you need to verify exact flag names, available commands, and supported package managers before constructing a command.

**status command** — per-tool install state and version:
```bash
winix status --no-color
```

Output lists each tool with its installed version (or `not installed`). Suitable for parsing in scripts when combined with `--no-color` to strip ANSI codes.
