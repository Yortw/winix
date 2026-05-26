# when — AI Agent Guide

## What This Tool Does

`when` converts timestamps between formats, applies time arithmetic, and calculates durations. It accepts Unix epochs (seconds or milliseconds), ISO 8601 dates and datetimes, and named-month dates. It outputs UTC, local, epoch, and relative representations — or a duration breakdown in diff mode.

## Platform Story

Cross-platform. **Windows has no native date conversion command.** `date` exists on Linux/macOS but its syntax for conversion and arithmetic is arcane and non-portable. `when` provides a clean, consistent interface on all platforms — replacing web tools, PowerShell one-liners, and shell gymnastics with a single binary.

## When to Use This

- Converting a Unix epoch from a log file or API response to a readable date: `when 1718745600`
- Calculating what date is N days from now: `when now +7d`
- Finding the duration between two dates: `when diff 2024-06-18 2024-06-25`
- Getting a pipe-friendly UTC timestamp: `when now --utc`
- Converting epochs from a list of records: `cat epochs.txt | wargs when`
- Checking what time it is in another timezone: `when now --tz America/New_York`

Prefer `when` over `date -d` on Linux (non-portable), PowerShell's `[datetime]` (verbose), or web tools (not scriptable).

## Common Patterns

**Convert an epoch to all formats:**
```bash
when 1718745600
```

**Show current time:**
```bash
when now
```

**Add time — days, hours, minutes:**
```bash
when now +7d
when now +2h30m
when now -90s
```

**ISO 8601 duration offset:**
```bash
when now +P1DT12H
```

**Timezone conversion:**
```bash
when now --tz Asia/Tokyo
when now --tz "Eastern Standard Time"
```

**Pipe-friendly single-value output:**
```bash
when now --utc          # 2024-06-18T16:00:00Z
when now --local        # 2024-06-18T04:00:00+12:00
```

**Duration between two timestamps:**
```bash
when diff 2024-06-18 2024-06-25
when diff now 2024-12-25 --iso
```

**JSON output for scripting:**
```bash
when 1718745600 --json
when diff 2024-06-18 2024-06-25 --json
```

## Composing with Other Tools

**when + wargs** — convert a list of epochs from stdin:
```bash
cat epochs.txt | wargs when
```

**when + timeit** — time the conversion:
```bash
timeit when diff 2024-01-01 2024-12-31
```

**when --utc + shell capture** — get current UTC timestamp in a script:
```bash
NOW=$(when now --utc)
```

**when --json + jq** — extract a specific field:
```bash
when 1718745600 --json | jq '.relative'
```

## Input Formats Accepted

| Format | Example |
|--------|---------|
| Unix epoch (seconds) | `1718745600` |
| Unix epoch (milliseconds) | `1718745600000` (13 digits) |
| ISO 8601 date | `2024-06-18` |
| ISO 8601 datetime | `2024-06-18T16:00:00Z` |
| ISO 8601 with offset | `2024-06-18T04:00:00+12:00` |
| Named month | `18 Jun 2024` |
| `now` keyword | `now` |

**Ambiguous formats are rejected.** `06/07/2024` is ambiguous (month-day-year vs day-month-year), so `when` returns a usage error rather than silently guessing. Use ISO 8601 or named-month formats to be unambiguous.

**Bare year-shaped values rejected as ambiguous.** Any value whose integer part is `1900-2200` with ≤4 digits is rejected — covers `2025`, `+2025`, `-2025`, `2025.0`. Use `2025-01-01` to mean the year, or pad to 5+ digits with leading zeros (`02025`) to force epoch interpretation.

## Offset Format Details

| Syntax | Example | Notes |
|--------|---------|-------|
| Shorthand | `+7d`, `-2h30m`, `+500ms`, `+1d12h30m` | Combinable single-unit chunks |
| ISO 8601 | `+P1DT12H`, `-P30D` | Standard duration; Y/M/W rejected |
| Time span | `+01:30:00` | HH:MM:SS |

Shorthand units: `ms` (milliseconds), `s` (seconds), `m` (minutes), `h` (hours), `d` (days), `w` (weeks). Each chunk is a non-negative integer plus a suffix; chunks combine in any order: `+1d2h30m15s`, `+2w3d`. The leading `+`/`-` applies to the whole offset.

## Diff Mode

`when diff <time1> <time2>` calculates the signed duration from time1 to time2. Negative duration means time2 is before time1.

Default output shows the breakdown and the ISO 8601 duration. Use `--iso` for just the ISO string, `--json` for structured data.

## Gotchas

**11-13 digit epochs are treated as milliseconds.** `1718745600000` (13 digits) and any 11-13 digit value are auto-detected as ms. 1-10 digit values are seconds. Negative values are always seconds (no millisecond ambiguity).

**Ambiguous formats are rejected, not guessed.** This is deliberate — silent wrong answers cause subtle bugs. Use unambiguous formats.

**`--utc` and `--local` are conversion-mode only.** They return an error in diff mode. Use `--iso` for diff mode single-value output.

**`--iso` is diff-mode only.** It returns an error in conversion mode.

**`--utc`, `--local`, and `--iso` are mutually exclusive.** Only one can be specified at a time.

**`now` means the instant the tool starts.** In diff mode, if both sides use `now`, the duration will be near-zero (not exactly zero due to sequential evaluation).

## Getting Structured Data

**Conversion mode JSON fields:**
- `tool`, `version`, `exit_code`, `exit_reason`
- `input` — original input string
- `offset` — offset string if applied, or null
- `utc` — UTC ISO 8601
- `local` — local ISO 8601
- `local_timezone` — local timezone abbreviation
- `unix_seconds`, `unix_milliseconds` — epoch values
- `relative` — human-readable relative time ("3 hours ago", "in 7 days")

**Diff mode JSON fields:**
- `tool`, `version`, `exit_code`, `exit_reason`
- `from`, `to` — both timestamps in UTC ISO 8601
- `duration_iso` — signed ISO 8601 duration
- `total_seconds` — signed total seconds
- `days`, `hours`, `minutes`, `seconds` — components

**--describe** — machine-readable flag reference:
```bash
when --describe
```
