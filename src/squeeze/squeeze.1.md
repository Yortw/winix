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
:   Keep the original file (default; accepted for gzip compatibility).

**-v**, **--verbose**
:   Show stats even when piped.

**-q**, **--quiet**
:   Suppress stats even on a terminal.

**--json**
:   JSON output to stderr.

**--color**
:   Force coloured output.

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

# EXIT CODES

**0**
:   Success.

**1**
:   Compression or decompression error.

**2**
:   Usage error (bad arguments).

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
