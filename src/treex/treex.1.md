% TREEX(1) Winix | User Commands
% Troy Willmot
% 2026-04-11

# NAME

treex - enhanced directory tree with colour, filtering, and size rollups

# SYNOPSIS

**treex** [*options*] [*paths*...]

# DESCRIPTION

Walks one or more directories (default: **.** ) and renders a tree-formatted listing to stdout. Supports glob and regex filtering, size rollups, date display, sort order, gitignore, and clickable terminal hyperlinks.

A cross-platform **tree** replacement with full ANSI colour and filtering capabilities.

# OPTIONS

**-g**, **--glob** *PATTERN*
:   Match filenames against a glob pattern (repeatable).

**-e**, **--regex** *PATTERN*
:   Match filenames against a regex (repeatable).

**--ext** *EXT*
:   Match file extension, e.g. **cs**, **log** (repeatable).

**-t**, **--type** *TYPE*
:   Filter by type: **f** (file), **d** (directory), **l** (symlink).

**--min-size** *SIZE*
:   Minimum file size (e.g. **100k**, **10M**, **1G**).

**--max-size** *SIZE*
:   Maximum file size (e.g. **100k**, **10M**).

**--newer** *DURATION*
:   Modified within *DURATION* (e.g. **1h**, **30m**, **7d**).

**--older** *DURATION*
:   Not modified within *DURATION* (e.g. **1h**, **7d**).

**-d**, **--max-depth** *N*
:   Maximum directory depth (0 = root only).

**--no-hidden**
:   Skip hidden files and directories.

**--gitignore**
:   Respect **.gitignore** rules.

**-i**, **--ignore-case**
:   Case-insensitive name matching.

**--case-sensitive**
:   Case-sensitive name matching.

**-s**, **--size**
:   Show file sizes.

**--date**
:   Show last-modified dates.

**--sort** *MODE*
:   Sort order: **name** (default), **size**, **modified**.

**-D**, **--dirs-only**
:   Show only directories.

**--no-links**
:   Disable clickable terminal hyperlinks.

**--ndjson**
:   Streaming NDJSON to stdout, one JSON object per node.

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

**--min-size** and **--max-size** accept an optional unit suffix: **b** (bytes), **k** (1024 bytes), **M** (megabytes), **G** (gigabytes). No suffix means bytes.

# DURATION UNITS

**--newer** and **--older** accept a number followed by a unit: **s** (seconds), **m** (minutes), **h** (hours), **d** (days), **w** (weeks).

# EXIT CODES

**0**
:   Success.

**1**
:   Runtime error (permission denied, invalid path).

**125**
:   Usage error (bad arguments).

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output. Clickable hyperlinks are also disabled when colour is off (no-color.org).

# EXAMPLES

    treex

    treex src --ext cs

    treex --size --gitignore --no-hidden

    treex --size --sort size

    treex -d 2

    treex src tests

    treex --ndjson | jq '.name'

# SEE ALSO

**files**(1), **wargs**(1), **timeit**(1), **man**(1)
