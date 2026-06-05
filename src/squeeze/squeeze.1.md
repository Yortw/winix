% SQUEEZE(1) Winix | User Commands
% Troy Willmot
% 2026-04-11

# NAME

squeeze - compress and decompress files

# SYNOPSIS

**squeeze** [*options*] [*file*...]

# DESCRIPTION

Compress and decompress files using gzip, brotli, or zstd. Supports pipe mode (stdin/stdout), multi-file batch processing, and gzip-compatible flags.

With no files, reads from stdin and writes to stdout (pipe mode). Output format defaults to gzip unless **--brotli** or **--zstd** is given.

Decompression (**-d**) auto-detects the format from magic bytes — the file extension is not required.

# OPTIONS

**-d**, **--decompress**
:   Decompress (auto-detects format from magic bytes).

**-b**, **--brotli**
:   Use brotli format.

**-z**, **--zstd**
:   Use zstd format.

**--level** *N*
:   Compression level (see COMPRESSION LEVELS).

**-1** .. **-9**
:   Shortcut for **--level 1** .. **--level 9**.

**-c**, **--stdout**
:   Write to stdout instead of creating an output file.

**-o**, **--output** *FILE*
:   Explicit output file (single input only; **-** for stdout).

**-f**, **--force**
:   Overwrite existing output files.

**--remove**
:   Delete the input file after a successful operation.

**-k**, **--keep**
:   Keep the original file (default). Takes precedence over **--remove** when both are supplied; a warning is emitted to stderr.

**-v**, **--verbose**
:   Show stats even when piped.

**-q**, **--quiet**
:   Suppress stats even on a terminal.

**--json**
:   JSON output to stderr.

**--color**[=_WHEN_]
:   Coloured output: auto (default when omitted), always, or never.

**--no-color**
:   Disable coloured output.

**--version**
:   Show version.

**-h**, **--help**
:   Show help.

# COMPRESSION LEVELS

| Format | Range | Default | Notes                           |
|--------|-------|---------|---------------------------------|
| gzip   | 1-9   | 6       | Standard deflate                |
| brotli | 0-11  | 6       | Higher levels much slower       |
| zstd   | 1-22  | 3       | Fast default, excellent at high |

# WILDCARDS

On Windows, cmd.exe and PowerShell do not expand **\*** and **?** wildcards before starting programs, so **squeeze** expands them itself. **squeeze \*.log** works the same as in bash. **[...]** is matched literally (brackets are legal Windows filename characters). **\*\*** is rejected with a usage error — use Git Bash for recursive patterns. A pattern that matches nothing is passed through unchanged so the normal "not found" error follows. In cmd, quoting a pattern (e.g. _"\*.log"_) suppresses expansion. On Linux and macOS the shell expands wildcards as usual and **squeeze** does nothing extra.

# EXIT CODES

**0**
:   Success.

**1**
:   Compression or decompression error: corrupt input, truncated gzip stream (ISIZE mismatch), multi-member gzip (concatenated gzip is rejected — see BUGS), unknown format, write failed.

**2**
:   Usage error: bad arguments, missing input, **--brotli** with **--zstd**, **--output** empty/whitespace, **--output** with multiple inputs, level out of range.

# BUGS

Multi-member gzip (concatenated gzip streams produced by `cat a.gz b.gz` or `gzip file1 file2 && cat *.gz`) is rejected with exit 1 and "data is corrupt or truncated", even though the decompressed content is correct. This is because squeeze validates the final ISIZE field against the cumulative decompressed byte count, and for multi-member streams ISIZE only represents the last member's size.

Workaround: pipe through system gzip (`gzip -dc concat.gz`) which handles multi-member natively, or split the concatenation back into individual `.gz` files.

This trade-off was made to prefer loud false-positives on rare multi-member input over silent corruption on common incompressible-truncation input. A future release may add proper member-by-member parsing.

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# EXAMPLES

    squeeze data.csv

    squeeze -d data.csv.gz

    squeeze --brotli --level 11 largefile.bin

    squeeze --zstd data.csv

    cat data.csv | squeeze > data.csv.gz

    squeeze -c -d archive.gz | head -20

    squeeze -o compressed.gz data.csv

    squeeze -9 data.csv

    squeeze *.log

# SEE ALSO

**timeit**(1), **peep**(1), **wargs**(1), **files**(1), **man**(1)
