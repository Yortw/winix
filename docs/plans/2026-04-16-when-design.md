# when — Date/Time Conversion and Arithmetic

**Date:** 2026-04-16
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`when` converts timestamps between formats, applies time arithmetic, and calculates durations between two points in time.

**Why it's needed:** Windows has no built-in date conversion command. Unix `date` exists but is arcane (`date -d @1718745600 +%Y-%m-%dT%H:%M:%S` — who remembers that?). Converting between epoch, ISO 8601, and human-readable formats is a daily task for developers inspecting logs, APIs, and databases. `when` replaces ad-hoc web tools and shell gymnastics with a single cross-platform command.

**Primary use cases:**
- Decode a Unix epoch from a log: `when 1718745600`
- Get current time in another timezone: `when now --tz Asia/Tokyo`
- Calculate a future date: `when now +7d`
- Measure gap between two timestamps: `when diff 2024-06-18 2024-06-25`
- Pipe UTC timestamp into a script: `when now --utc`

**Platform:** Cross-platform (Windows, Linux, macOS). No platform-specific code. Requires ICU for timezone support (InvariantGlobalization disabled for this tool only).

---

## Project Structure

```
src/Winix.When/        — class library (parsing, conversion, formatting)
src/when/              — thin console app (arg parsing, output)
tests/Winix.When.Tests/ — xUnit tests
```

Standard Winix conventions: library does all work, console app is thin.

---

## CLI Interface

### Conversion Mode (default)

```
when [options] <input> [+/-offset]
```

Parses `<input>` as a timestamp, optionally applies an offset, and displays the result.

```bash
when 1718745600                          # epoch → all formats
when now                                 # current time
when 2024-06-18T20:00:00Z               # ISO 8601
when "Jun 18 2024"                       # named-month format
when "2024-06-18 20:00:00"              # space-separated ISO-like
when now +7d                             # now plus 7 days
when 1718745600 -3h                      # epoch minus 3 hours
when now +P1DT12H                        # ISO 8601 duration offset
when now +1.02:30:00                     # .NET TimeSpan offset
when now +01:30:00                       # HH:MM:SS offset
when now --tz Asia/Tokyo                 # show in specific timezone
when now --tz "Tokyo Standard Time"      # Windows timezone ID also works
when now +7d --tz Asia/Tokyo             # arithmetic + timezone
```

### Diff Mode

```
when diff <time1> <time2> [options]
```

Calculates the duration between two timestamps.

```bash
when diff 1718745600 1718832000          # epoch to epoch
when diff 2024-06-18 2024-06-25         # date to date
when diff now 2024-12-25                # time until Christmas
when diff now 2024-12-25 --tz Asia/Tokyo # timezone applied to display
when diff now 2024-12-25 --iso          # ISO 8601 duration output only
```

---

## Options

| Flag | Default | Description |
|------|---------|-------------|
| `--tz ZONE` | (none) | Display output in this timezone. Accepts IANA (`Asia/Tokyo`) or Windows (`Tokyo Standard Time`) IDs. |
| `--utc` | off | Output only the UTC ISO 8601 string. Conversion mode only. |
| `--local` | off | Output only the local-time ISO 8601 string. If `--tz` is also set, uses that timezone instead of system local. |
| `--iso` | off | Output only the ISO 8601 duration string. Diff mode only. |
| `--json` | off | Full JSON output. |
| `--color`/`--no-color` | auto | Colour control. Respects `NO_COLOR` env var. |
| `--help` / `-h` | | Help text. |
| `--version` / `-v` | | Version. |
| `--describe` | | AI-readable tool description. |

**Mutual exclusion:** `--utc`, `--local`, and `--iso` are mutually exclusive. Using more than one is a usage error. `--utc` and `--local` are invalid in diff mode; `--iso` is invalid in conversion mode.

**`--tz` interaction with `--local`:** When both are specified, `--local` outputs in the `--tz` timezone (not system local). This is intentional — `when now --local --tz Asia/Tokyo` gives a single Tokyo-time ISO string.

---

## Output to stdout

Unlike tools that wrap child processes (timeit, retry), `when` has no child stdout to protect. All output goes to **stdout** by default. Errors go to stderr. No `--stdout` flag is needed.

---

## Input Formats

### Timestamp Detection Order

The `<input>` argument is parsed in this order (first match wins):

1. **`now`** keyword — `DateTimeOffset.UtcNow`
2. **Unix epoch** — bare integer or decimal (`1718745600`, `1718745600.123`). Integers up to 10 digits are seconds; 11-13 digits are milliseconds (JavaScript `Date.now()` convention). Decimals are always seconds. See Epoch Auto-Detection below for full rules.
3. **ISO 8601 datetime** — `2024-06-18T20:00:00Z`, `2024-06-18T20:00:00+12:00`, `2024-06-18` (date only → midnight UTC)
4. **Space-separated ISO-like** — `2024-06-18 20:00:00`, `2024-06-18 20:00:00+12:00` (common in SQL output and log files)
5. **Named-month formats** — `Jun 18 2024`, `18 Jun 2024`, `Jun 18, 2024` (month name removes DD/MM ambiguity)

Ambiguous numeric-only formats (`06/12/2024`, `12-06-2024`) are **rejected** with an error suggesting ISO format. This avoids the DD/MM vs MM/DD trap.

### Epoch Auto-Detection

To distinguish "is this a Unix epoch or a year?":
- Values ≤ 9999 are ambiguous — could be year 2024 or epoch 2024 (Jan 1 1970 00:33:44). Treat as epoch (the year-only case is handled by ISO format `2024-01-01`).
- Values ≥ 10000 and ≤ 9999999999 (10 digits) — seconds since epoch.
- Values ≥ 10000000000 (11+ digits) and ≤ 9999999999999 (13 digits) — milliseconds since epoch.
- Negative values — seconds before epoch (valid Unix timestamps, e.g. `-62135596800` = 0001-01-01).

### Offset Formats

The `+`/`-` prefix is **required** to distinguish offsets from timestamps. Detection order:

1. **Simple duration** — `+7d`, `-3h`, `+1w2d`, `+500ms`. Via ShellKit's `DurationParser`.
2. **ISO 8601 duration** — `+P3DT4H12M`, `-PT1H30M`. Custom parser (see below).
3. **.NET TimeSpan** — `+1.02:30:00` (d.HH:MM:SS). Via `TimeSpan.TryParseExact` with `c` and `g` formats.
4. **HH:MM:SS** — `+01:30:00`, `-00:05:30`. Via `TimeSpan.TryParseExact`.

The sign (`+`/`-`) is stripped before parsing the duration value, then applied to the result.

---

## Default Output (Conversion Mode)

```
  UTC:       2024-06-18T20:00:00Z
  Local:     2024-06-19 08:00:00 NZST (+12:00)
  Relative:  11 months ago
  Unix:      1718745600
```

With `--tz Asia/Tokyo`:
```
  UTC:       2024-06-18T20:00:00Z
  Local:     2024-06-19 08:00:00 NZST (+12:00)
  Tokyo:     2024-06-19 05:00:00 JST (+09:00)
  Relative:  11 months ago
  Unix:      1718745600
```

Labels are coloured with dim (like timeit's output). The timezone line label is derived from the timezone's display name (shortened to city if IANA, or the standard name abbreviation).

### Single-Value Outputs

- `--utc`: `2024-06-18T20:00:00Z` (no newline padding, pipe-friendly)
- `--local`: `2024-06-19T08:00:00+12:00` (or `--tz` timezone if specified)

### Relative Time Display

| Range | Format |
|-------|--------|
| < 1 minute | "just now" / "moments ago" |
| < 1 hour | "12 minutes ago" / "in 12 minutes" |
| < 1 day | "3 hours ago" / "in 3 hours" |
| < 30 days | "7 days ago" / "in 7 days" |
| < 365 days | "3 months ago" / "in 3 months" |
| ≥ 365 days | "2 years ago" / "in 2 years" |

Uses the largest appropriate unit. "ago" for past, "in X" for future.

---

## Default Output (Diff Mode)

```
  Duration:  7 days, 4 hours, 12 minutes
  ISO 8601:  P7DT4H12M
  Seconds:   619920
```

With `--tz`:
```
  From:      2024-06-18 00:00:00 JST (+09:00)
  To:        2024-06-25 00:00:00 JST (+09:00)
  Duration:  7 days
  ISO 8601:  P7DT0H0M
  Seconds:   604800
```

**Human output:** The duration is always positive (absolute value). The "From" and "To" lines show whichever is earlier first. This makes the default output intuitive at a glance.

**Machine output (`--iso`, `--json`):** The duration is **signed**, preserving the argument order. If time1 is before time2 the duration is positive; if time1 is after time2 it's negative. This lets scripts detect direction without comparing the inputs themselves.

### Single-Value Output

- `--iso`: `P7DT4H12M` (positive if time1 < time2) or `-P7DT4H12M` (negative if time1 > time2)

### Duration Display

The human-friendly format shows the largest units that apply, down to seconds:
- `7 days, 4 hours, 12 minutes`
- `1 hour, 30 seconds`
- `45 seconds`
- `250 milliseconds` (only if sub-second)

---

## JSON Output

### Conversion Mode

```json
{
  "tool": "when",
  "version": "0.3.0",
  "exit_code": 0,
  "exit_reason": "success",
  "input": "1718745600",
  "offset": null,
  "utc": "2024-06-18T20:00:00Z",
  "local": "2024-06-19T08:00:00+12:00",
  "local_timezone": "NZST",
  "unix_seconds": 1718745600,
  "unix_milliseconds": 1718745600000,
  "relative": "11 months ago"
}
```

With `--tz`, adds:
```json
  "target_timezone": "JST",
  "target": "2024-06-19T05:00:00+09:00"
```

### Diff Mode

Values are **signed** — positive when time2 is after time1, negative when time2 is before time1. `from` and `to` preserve the user's argument order (not reordered).

```json
{
  "tool": "when",
  "version": "0.3.0",
  "exit_code": 0,
  "exit_reason": "success",
  "from": "2024-06-18T00:00:00Z",
  "to": "2024-06-25T00:00:00Z",
  "duration_iso": "P7DT0H0M",
  "total_seconds": 604800,
  "days": 7,
  "hours": 0,
  "minutes": 0,
  "seconds": 0
}
```

When time1 is after time2 (negative duration):

```json
{
  "tool": "when",
  "version": "0.3.0",
  "exit_code": 0,
  "exit_reason": "success",
  "from": "2024-06-25T00:00:00Z",
  "to": "2024-06-18T00:00:00Z",
  "duration_iso": "-P7DT0H0M",
  "total_seconds": -604800,
  "days": -7,
  "hours": 0,
  "minutes": 0,
  "seconds": 0
}
```

### Error JSON

```json
{
  "tool": "when",
  "version": "0.3.0",
  "exit_code": 125,
  "exit_reason": "parse_error",
  "message": "Cannot parse '06/12/2024' — ambiguous date format. Use ISO 8601: 2024-06-12 or 2024-12-06"
}
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 125 | Usage error — bad arguments, unparseable input, unknown timezone, mutually exclusive flags |

No 126/127 — `when` does not spawn child processes.

---

## Components

### InputParser

Detects and parses all timestamp input formats. Returns a `DateTimeOffset`.

```csharp
public static class InputParser
{
    /// <summary>
    /// Parses a timestamp string, trying formats in priority order.
    /// </summary>
    /// <param name="input">The raw input string.</param>
    /// <param name="result">The parsed timestamp, or default if parsing failed.</param>
    /// <param name="error">A human-readable error message if parsing failed.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(string input, out DateTimeOffset result, out string? error);
}
```

### OffsetParser

Parses duration/offset strings (with `+`/`-` prefix stripped by caller).

```csharp
public static class OffsetParser
{
    /// <summary>
    /// Parses a duration string, trying all supported formats.
    /// </summary>
    /// <param name="input">The duration string without sign prefix.</param>
    /// <param name="result">The parsed duration.</param>
    /// <param name="error">A human-readable error message if parsing failed.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(string input, out TimeSpan result, out string? error);
}
```

### IsoDurationParser

Parses ISO 8601 duration strings (`P3DT4H12M`). Supports days and below only (no years/months — those are calendar-dependent). Also formats TimeSpan → ISO 8601 duration string.

```csharp
public static class IsoDurationParser
{
    /// <summary>
    /// Parses an ISO 8601 duration (PnDTnHnMnS) to a TimeSpan.
    /// Years and months are not supported (calendar-dependent).
    /// </summary>
    public static bool TryParse(string input, out TimeSpan result, out string? error);

    /// <summary>
    /// Formats a TimeSpan as an ISO 8601 duration string (PnDTnHnMnS).
    /// </summary>
    public static string Format(TimeSpan duration);
}
```

### TimezoneResolver

Wraps `TimeZoneInfo.FindSystemTimeZoneById` with clear error messages.

```csharp
public static class TimezoneResolver
{
    /// <summary>
    /// Resolves a timezone by IANA or Windows ID.
    /// </summary>
    /// <param name="id">IANA (e.g. "Asia/Tokyo") or Windows (e.g. "Tokyo Standard Time") timezone ID.</param>
    /// <param name="zone">The resolved timezone info.</param>
    /// <param name="error">Error message if the timezone was not found.</param>
    /// <returns>True if the timezone was found.</returns>
    public static bool TryResolve(string id, out TimeZoneInfo? zone, out string? error);
}
```

### RelativeFormatter

Formats a time difference as a human-friendly relative string.

```csharp
public static class RelativeFormatter
{
    /// <summary>
    /// Formats the duration between a timestamp and now as a relative string.
    /// </summary>
    /// <param name="timestamp">The timestamp to compare against now.</param>
    /// <param name="now">The current time (injected for testability).</param>
    /// <returns>"3 hours ago", "in 7 days", "just now", etc.</returns>
    public static string Format(DateTimeOffset timestamp, DateTimeOffset now);
}
```

### DurationFormatter

Formats a TimeSpan as human-friendly text and ISO 8601 duration.

```csharp
public static class DurationFormatter
{
    /// <summary>
    /// Formats a duration as a human-friendly string like "7 days, 4 hours, 12 minutes".
    /// </summary>
    public static string FormatHuman(TimeSpan duration);

    /// <summary>
    /// Formats a duration as ISO 8601 (PnDTnHnMnS).
    /// Alias for <see cref="IsoDurationParser.Format"/>.
    /// </summary>
    public static string FormatIso(TimeSpan duration);
}
```

### Formatting

Top-level formatting that composes the above into complete output blocks.

```csharp
public static class Formatting
{
    /// <summary>Multi-line default output for conversion mode.</summary>
    public static string FormatDefault(DateTimeOffset timestamp, TimeZoneInfo localTz,
        TimeZoneInfo? extraTz, DateTimeOffset now, bool useColor);

    /// <summary>Single UTC ISO 8601 string.</summary>
    public static string FormatUtc(DateTimeOffset timestamp);

    /// <summary>Single local (or --tz) ISO 8601 string.</summary>
    public static string FormatLocal(DateTimeOffset timestamp, TimeZoneInfo tz);

    /// <summary>Multi-line default output for diff mode.</summary>
    public static string FormatDiff(TimeSpan duration, DateTimeOffset from, DateTimeOffset to,
        TimeZoneInfo? displayTz, bool useColor);

    /// <summary>JSON for conversion mode.</summary>
    public static string FormatJson(DateTimeOffset timestamp, TimeZoneInfo localTz,
        TimeZoneInfo? extraTz, DateTimeOffset now, string? inputStr, string? offsetStr,
        string toolName, string version);

    /// <summary>JSON for diff mode.</summary>
    public static string FormatDiffJson(TimeSpan duration, DateTimeOffset from, DateTimeOffset to,
        string toolName, string version);

    /// <summary>JSON error.</summary>
    public static string FormatJsonError(int exitCode, string exitReason, string message,
        string toolName, string version);
}
```

---

## InvariantGlobalization

The `when` console app overrides the default AOT setting:

```xml
<InvariantGlobalization>false</InvariantGlobalization>
```

This enables ICU, which is required for:
- `TimeZoneInfo.FindSystemTimeZoneById` with IANA IDs on Windows
- Cross-platform IANA ↔ Windows timezone ID resolution
- Named-month parsing with `DateTimeOffset.TryParseExact`

The class library (`Winix.When`) does not set this flag — it's a consumer concern. All other Winix tools remain invariant.

**Binary size impact:** ~30MB increase for the AOT native binary on Windows (bundled ICU). Linux/macOS use system ICU so no size impact. The .NET global tool (JIT) is unaffected. This is acceptable for a tool whose core value is timezone-aware date conversion.

---

## Testing Strategy

Unit tests in `Winix.When.Tests`:

- **InputParser:** epoch (seconds, milliseconds, negative), ISO 8601 (with/without timezone, date-only), space-separated, named-month, `now`, ambiguous format rejection, edge cases (year 2024 vs epoch 2024)
- **OffsetParser:** simple duration (+7d, -3h), ISO 8601 duration (+P3DT4H12M), .NET TimeSpan (+1.02:30:00), HH:MM:SS (+01:30:00), sign handling, invalid formats
- **IsoDurationParser:** parse P3DT4H12M, PT1H30M, P7D, PT0S; reject P1Y, P1M; format round-trip
- **TimezoneResolver:** IANA ID, Windows ID, unknown ID error
- **RelativeFormatter:** just now, minutes ago, hours ago, days ago, months ago, years ago, future variants, boundary cases
- **DurationFormatter:** human format (days+hours+minutes, hours+seconds, milliseconds), ISO format
- **Formatting:** default multi-line (with/without --tz, with/without color), --utc, --local, diff default, diff --iso, JSON conversion, JSON diff, JSON error

Integration tests are not needed — no child processes, no I/O beyond stdout.

---

## Composability

| Composition | Example | Effect |
|-------------|---------|--------|
| when → variable | `` DEPLOY_AT=$(when now +2h --utc) `` | Capture a future UTC timestamp for scripting |
| when → log | `echo "Built at $(when now --utc)"` | Timestamp a log line |
| when + timeit | `timeit when diff $START now` | Time the diff calculation (trivial, but consistent) |
| when + wargs | `cat epochs.txt \| wargs when` | Convert a list of epochs |
| when + jq | `when now --json \| jq '.unix_seconds'` | Extract specific field |
