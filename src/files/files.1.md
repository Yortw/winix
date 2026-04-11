% FILES(1) Winix | User Commands
% Troy Willmot
% 2026-04-11

# NAME

files - find files by name, size, date, type, and content

# SYNOPSIS

**files** [*options*] [*paths*...]

# DESCRIPTION

Walks one or more directories (default: **.** ) and prints matching file paths to stdout, one per line. Supports glob and regex name matching, type filtering, size and date ranges, text/binary detection, and structured output.

Results are printed to stdout; the **--json** summary goes to stderr so it does not pollute piped output.

# OPTIONS

**-g**, **--glob** *PATTERN*
:   Match filenames against a glob pattern (repeatable).

**-e**, **--regex** *PATTERN*
:   Match filenames against a regex (repeatable).

**--ext** *EXT*
:   Match file extension, e.g. **cs**, **log** (repeatable).

**-t**, **--type** *TYPE*
:   Filter by type: **f** (file), **d** (directory), **l** (symlink).

**--text**
:   Only text files.

**--binary**
:   Only binary files.

**--min-size** *SIZE*
:   Minimum file size (e.g. **100k**, **10M**, **1G**).

**--max-size** *SIZE*
:   Maximum file size (e.g. **100k**, **10M**).

**--newer** *DURATION*
:   Modified within *DURATION* (e.g. **1h**, **30m**, **7d**).

**--older** *DURATION*
:   Not modified within *DURATION* (e.g. **1h**, **7d**).

**-d**, **--max-depth** *N*
:   Maximum directory depth (0 = search root only).

**-L**, **--follow**
:   Follow symbolic links.

**--absolute**
:   Output absolute paths.

**--no-hidden**
:   Skip hidden files and directories.

**--gitignore**
:   Respect **.gitignore** rules.

**-i**, **--ignore-case**
:   Case-insensitive name matching.

**--case-sensitive**
:   Case-sensitive name matching.

**-l**, **--long**
:   Tab-delimited detail output (path, size, date, type).

**-0**, **--print0**
:   Null-delimited output (for **xargs -0**).

**--ndjson**
:   Streaming NDJSON to stdout, one JSON object per file.

**--json**
:   JSON summary to stderr on exit.

**--describe**
:   Print machine-readable metadata and exit.

**--color**
:   Force coloured output.

**--no-color**
:   Disable coloured output.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

# SIZE UNITS

**--min-size** and **--max-size** accept an optional unit suffix: **k** (1024 bytes), **M** (megabytes), **G** (gigabytes). No suffix means bytes. Examples: **500**, **10k**, **2M**, **1G**.

# DURATION UNITS

**--newer** and **--older** accept a number followed by a unit: **s** (seconds), **m** (minutes), **h** (hours), **d** (days), **w** (weeks). Examples: **30m**, **1h**, **7d**.

# EXIT CODES

**0**
:   Success.

**1**
:   Runtime error (permission denied, invalid path).

**125**
:   Usage error (bad arguments).

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# EXAMPLES

    files src --ext cs

    files . --text --type f

    files . --newer 1h --type f

    files . --long --ext cs

    files . --gitignore --no-hidden --ext cs

    files . --glob '*.log' | wargs rm

    files . --ndjson | jq '.name'

    files . --min-size 1k --max-size 10M

    files . --glob '*.log' --print0 | xargs -0 rm

# SEE ALSO

**treex**(1), **wargs**(1), **timeit**(1), **man**(1)
