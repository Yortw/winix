% WHEN(1) Winix | User Commands
% Troy Willmot
% 2026-04-17

# NAME

when - convert timestamps between formats, apply time arithmetic, and calculate durations

# SYNOPSIS

**when** [*options*] *input* [*+/-offset*]

**when** [*options*] **diff** *time1* *time2*

# DESCRIPTION

Converts timestamps between formats, applies time arithmetic, and calculates durations. Cross-platform — fills the gap on Windows where no native date conversion command exists.

Two modes of operation:

- **Conversion mode**: parse a timestamp, optionally apply an offset, and output the result in all formats.
- **Diff mode**: calculate the duration between two timestamps.

# OPTIONS

**--tz** *ZONE*
:   Display in this timezone. Accepts both IANA timezone IDs (e.g. **America/New_York**) and Windows timezone IDs (e.g. **Eastern Standard Time**).

**--utc**
:   Output only the UTC ISO 8601 timestamp (conversion mode). Useful for scripting and piping.

**--local**
:   Output only the local ISO 8601 timestamp (conversion mode). When combined with **--tz**, uses the specified timezone instead of the system local timezone.

**--iso**
:   Output only the ISO 8601 duration string (diff mode).

**--json**
:   Output results as JSON to stdout.

**--color**
:   Force coloured output (overrides **NO_COLOR**).

**--no-color**
:   Disable coloured output.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

**--describe**
:   Emit machine-readable JSON metadata (flags, examples, composability).

# INPUT FORMATS

The following input formats are accepted:

- Unix epoch in seconds (auto-detected; 13-digit numbers are treated as milliseconds)
- ISO 8601 date: **2024-06-18**
- ISO 8601 datetime: **2024-06-18T16:00:00Z** (with or without timezone offset)
- Named month: **18 Jun 2024**
- The keyword **now** for the current instant

Ambiguous formats (e.g. **06/07/2024** where month/day order is unclear) are rejected with an error.

# OFFSET FORMATS

Offsets are prefixed with **+** or **-** and support three syntaxes:

- Duration shorthand: **+7d**, **-2h30m**, **+90s** (units: **d** days, **h** hours, **m** minutes, **s** seconds)
- ISO 8601 duration: **+P1DT12H**
- Time span: **+01:30:00** (HH:MM:SS)

# EXIT CODES

**0**
:   Success.

**125**
:   Usage error — bad arguments, unparseable input, unknown timezone.

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# EXAMPLES

    when 1718745600

    when now

    when now --tz Asia/Tokyo

    when now +7d

    when now --utc

    when diff 2024-06-18 2024-06-25

    when diff now 2024-12-25 --iso

    when 1718745600 --json

# SEE ALSO

**timeit**(1), **wargs**(1), **files**(1), **peep**(1)
