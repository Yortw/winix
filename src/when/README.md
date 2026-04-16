# when

Convert timestamps between formats, apply time arithmetic, and calculate durations. Cross-platform — fills the gap on Windows where no native date conversion command exists.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/when
```

### Winget (Windows, stable releases)

```bash
winget install Winix.When
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.When
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
when [options] <input> [+/-offset]
when [options] diff <time1> <time2>
```

### Examples

```bash
# Convert a Unix epoch to all formats
when 1718745600

# Show current time in all formats
when now

# Show current time in a specific timezone
when now --tz Asia/Tokyo

# Add 7 days to now
when now +7d

# Subtract 2 hours 30 minutes
when now -2h30m

# ISO 8601 duration offset
when now +P1DT12H

# Pipe-friendly UTC timestamp
when now --utc

# Calculate duration between two dates
when diff 2024-06-18 2024-06-25

# ISO 8601 duration until Christmas
when diff now 2024-12-25 --iso

# JSON output for scripting
when 1718745600 --json

# Convert a list of epochs via wargs
cat epochs.txt | wargs when
```

### Output

**Default** (all formats):
```
UTC:       2024-06-18T16:00:00Z
Local:     2024-06-18T04:00:00+12:00  (NZST)
Epoch:     1718726400  (1718726400000 ms)
Relative:  4 months ago
```

**Diff mode:**
```
Duration:  7 days  (P7D)
From:      2024-06-18T00:00:00Z
To:        2024-06-25T00:00:00Z
```

**JSON** (`--json`):
```json
{"tool":"when","version":"0.3.0","exit_code":0,"exit_reason":"success","input":"1718745600","utc":"2024-06-18T16:00:00Z","local":"2024-06-19T04:00:00+12:00","local_timezone":"NZST","unix_seconds":1718745600,"unix_milliseconds":1718745600000,"relative":"4 months ago"}
```

## Options

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--tz ZONE` | | (system) | Display in this timezone (IANA or Windows ID) |
| `--utc` | | off | Output only UTC ISO 8601 string (conversion mode) |
| `--local` | | off | Output only local ISO 8601 string (conversion mode) |
| `--iso` | | off | Output only ISO 8601 duration string (diff mode) |
| `--json` | | off | JSON output |
| `--color` | | auto | Force coloured output (overrides `NO_COLOR`) |
| `--no-color` | | auto | Disable coloured output |
| `--version` | `-v` | | Show version |
| `--help` | `-h` | | Show help |
| `--describe` | | | AI/agent metadata (JSON) |

### Input Formats

| Format | Example | Notes |
|--------|---------|-------|
| Unix epoch (seconds) | `1718745600` | Auto-detected |
| Unix epoch (milliseconds) | `1718745600000` | 13-digit numbers treated as ms |
| ISO 8601 date | `2024-06-18` | |
| ISO 8601 datetime | `2024-06-18T16:00:00Z` | With or without timezone offset |
| Named month | `18 Jun 2024` | |
| `now` | `now` | Current instant |

Ambiguous formats (e.g. `06/07/2024` where month/day order is unclear) are rejected with an error rather than silently guessing.

### Offset Formats

Offsets can be prefixed with `+` or `-`:

| Format | Example | Meaning |
|--------|---------|---------|
| Duration shorthand | `+7d`, `-2h30m`, `+90s` | Days, hours, minutes, seconds |
| ISO 8601 duration | `+P1DT12H` | Standard duration notation |
| Time of day | `+01:30:00` | HH:MM:SS |

Supported shorthand units: `d` (days), `h` (hours), `m` (minutes), `s` (seconds). Units can be combined: `+1d12h`.

### Timezone

`--tz` accepts both IANA timezone IDs (`America/New_York`, `Asia/Tokyo`) and Windows timezone IDs (`Eastern Standard Time`). Use `--tz UTC` for UTC output.

## Exit Codes

`when` exits 0 on success, 125 on usage errors (unparseable input, bad arguments, unknown timezone).

| Code | Meaning |
|------|---------|
| 0 | Success |
| 125 | Usage error — bad arguments, unparseable input, unknown timezone |

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`when` is part of the [Winix](../../README.md) CLI toolkit.
