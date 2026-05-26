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
:   Maximum directory depth (0-based; **0** = root only, **1** = root and immediate children, **N** = root and N levels of children).

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
:   JSON envelope to stdout on exit (suite convention).

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
:   Runtime error (permission denied, invalid path, or partial walk with one or more unreadable directories).

**125**
:   Usage error (bad arguments).

When directories cannot be enumerated (typically permission denied), each unreadable path is reported on stderr, the rendered tree marks the directory with **[error opening dir]**, and the process exits **1**.

# STRUCTURED OUTPUT

Both **--ndjson** and **--json** write to **stdout** per suite convention.

**--ndjson** emits one JSON object per node with fields **path** (relative to root, forward-slash separated), **name**, **type** (**file** | **dir** | **link**), **size_bytes** (integer, or **null** for directories without **--size** rollup), **modified** (ISO 8601), and **depth** (integer; **0** = root).

**--json** emits a single envelope after the walk: **tool**, **version**, **exit_code**, **exit_reason**, **directories**, **files**, **walk_errors** (array of `{path, reason}` for unreadable paths; empty on success), and (when **--size** is on) **total_size_bytes**. Pre-walk error envelopes (path_not_found, not_a_directory) additionally carry **error** (human-readable detail).

The **exit_reason** values are: **success**, **walk_error_partial**, **path_not_found**, **not_a_directory**, **usage_error**, **runtime_error**.

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
